"""FastAPI application factory, routers, and handlers."""

import asyncio
import hashlib
import hmac
import json
import logging
from pathlib import Path
import tempfile
import time
from uuid import UUID

from fastapi import APIRouter, Depends, FastAPI, File, Form, Header, Request, UploadFile
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.config import Settings
from app.errors import ServiceError
from app.pipeline.asr import StubTranscriber, Transcriber
from app.pipeline.audio import check_duration, normalize, probe
from app.schemas import (
    AnalyzeMeta,
    AnalyzeResponse,
    AnalyzeStats,
    AsrInfo,
    DetectRequest,
    DetectResponse,
    HealthResponse,
)
from app.version import PIPELINE_VERSION

logger = logging.getLogger(__name__)


def get_transcriber() -> Transcriber:
    return StubTranscriber()


def create_app(settings: Settings | None = None) -> FastAPI:
    if settings is None:
        settings = Settings()

    app = FastAPI(title="AI Service", version=PIPELINE_VERSION)
    app.state.settings = settings

    # Exception handlers
    @app.exception_handler(ServiceError)
    async def service_error_handler(request: Request, exc: ServiceError) -> JSONResponse:
        return JSONResponse(
            status_code=exc.http_status,
            content={"error_code": exc.code, "message": exc.message},
        )

    @app.exception_handler(RequestValidationError)
    async def validation_exception_handler(request: Request, exc: RequestValidationError) -> JSONResponse:
        return JSONResponse(
            status_code=422,
            content={"error_code": "INVALID_REQUEST", "message": "Invalid request parameters"},
        )

    @app.exception_handler(StarletteHTTPException)
    async def http_exception_handler(request: Request, exc: StarletteHTTPException) -> JSONResponse:
        error_code = "NOT_FOUND" if exc.status_code == 404 else "HTTP_ERROR"
        return JSONResponse(
            status_code=exc.status_code,
            content={"error_code": error_code, "message": str(exc.detail)},
        )

    @app.exception_handler(Exception)
    async def generic_exception_handler(request: Request, exc: Exception) -> JSONResponse:
        logger.exception("Internal server error")
        return JSONResponse(
            status_code=500,
            content={"error_code": "INTERNAL_ERROR", "message": "Internal server error"},
        )

    # Health check route (no auth)
    @app.get("/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        return HealthResponse(status="ok", pipeline_version=PIPELINE_VERSION)

    # Auth dependency for /v1
    def verify_service_key(
        x_service_key: str | None = Header(None, alias="X-Service-Key"),
    ) -> None:
        if not x_service_key:
            raise ServiceError(code="UNAUTHORIZED", http_status=401, message="Missing service key")
        if not hmac.compare_digest(x_service_key.encode("utf-8"), settings.AI_SERVICE_KEY.encode("utf-8")):
            raise ServiceError(code="UNAUTHORIZED", http_status=401, message="Invalid service key")

    v1_router = APIRouter(prefix="/v1", dependencies=[Depends(verify_service_key)])

    @v1_router.post("/analyze", response_model=AnalyzeResponse)
    async def analyze(
        audio: UploadFile = File(...),
        meta: str = Form(...),
        transcriber: Transcriber = Depends(get_transcriber),
    ) -> AnalyzeResponse:
        start_time = time.perf_counter()
        recording_id: UUID | None = None
        duration_ms = 0

        try:
            try:
                meta_dict = json.loads(meta)
                analyze_meta = AnalyzeMeta.model_validate(meta_dict)
                recording_id = analyze_meta.recording_id
            except Exception as exc:
                raise ServiceError(code="INVALID_META", http_status=422, message="Invalid metadata") from exc

            if settings.TMP_DIR is not None:
                Path(settings.TMP_DIR).mkdir(parents=True, exist_ok=True)

            with tempfile.TemporaryDirectory(prefix="ai-svc-", dir=settings.TMP_DIR) as tmp_dir:
                input_path = Path(tmp_dir) / "input.bin"
                normalized_path = Path(tmp_dir) / "normalized.wav"

                hasher = hashlib.sha256()
                total_bytes = 0
                chunk_size = 1024 * 1024  # 1 MiB

                with open(input_path, "wb") as f:
                    while True:
                        chunk = await audio.read(chunk_size)
                        if not chunk:
                            break
                        total_bytes += len(chunk)
                        if total_bytes > settings.MAX_UPLOAD_BYTES:
                            raise ServiceError(
                                code="AUDIO_TOO_LARGE",
                                http_status=413,
                                message="Uploaded audio exceeds maximum allowed size",
                            )
                        hasher.update(chunk)
                        f.write(chunk)

                source_sha256 = hasher.hexdigest().lower()

                source_info = await asyncio.to_thread(
                    probe,
                    input_path,
                    ffprobe_bin=settings.FFPROBE_BIN,
                    timeout_s=settings.FFMPEG_TIMEOUT_SECONDS,
                )
                duration_ms = source_info.duration_ms
                check_duration(
                    source_info,
                    min_seconds=settings.MIN_AUDIO_SECONDS,
                    max_seconds=settings.MAX_AUDIO_SECONDS,
                )

                norm_info = await asyncio.to_thread(
                    normalize,
                    input_path,
                    normalized_path,
                    ffmpeg_bin=settings.FFMPEG_BIN,
                    ffprobe_bin=settings.FFPROBE_BIN,
                    timeout_s=settings.FFMPEG_TIMEOUT_SECONDS,
                )
                duration_ms = norm_info.duration_ms

                segments = await asyncio.to_thread(
                    transcriber.transcribe,
                    normalized_path,
                    norm_info.duration_ms,
                )

                processing_ms = int((time.perf_counter() - start_time) * 1000)
                logger.info(
                    "recording_id=%s duration_ms=%d processing_ms=%d outcome=SUCCESS",
                    recording_id,
                    duration_ms,
                    processing_ms,
                )

                return AnalyzeResponse(
                    pipeline_version=PIPELINE_VERSION,
                    stage="W1_RAW",
                    asr=AsrInfo(
                        model=transcriber.model_name,
                        language="vi",
                        duration_ms=norm_info.duration_ms,
                        channels=norm_info.channels,
                    ),
                    segments=segments,
                    stats=AnalyzeStats(
                        pii_masked=0,
                        guardrail_dropped=0,
                        processing_ms=processing_ms,
                        source_sample_rate=source_info.sample_rate,
                        source_channels=source_info.channels,
                        source_sha256=source_sha256,
                    ),
                )
        except ServiceError as exc:
            processing_ms = int((time.perf_counter() - start_time) * 1000)
            rec_id_str = str(recording_id) if recording_id else "UNKNOWN"
            logger.info(
                "recording_id=%s duration_ms=%d processing_ms=%d outcome=%s",
                rec_id_str,
                duration_ms,
                processing_ms,
                exc.code,
            )
            raise
        except Exception:
            processing_ms = int((time.perf_counter() - start_time) * 1000)
            rec_id_str = str(recording_id) if recording_id else "UNKNOWN"
            logger.info(
                "recording_id=%s duration_ms=%d processing_ms=%d outcome=INTERNAL_ERROR",
                rec_id_str,
                duration_ms,
                processing_ms,
            )
            raise

    @v1_router.post("/detect", response_model=DetectResponse)
    async def detect(payload: DetectRequest) -> DetectResponse:
        return DetectResponse(
            pipeline_version=PIPELINE_VERSION,
            stage="W1_STUB",
            kb_version=None,
            service_spans=[],
            flags=[],
        )

    app.include_router(v1_router)
    return app
