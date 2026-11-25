# AI Service (`ai-service/`)

FastAPI microservice for audio normalization (16 kHz mono WAV) and transaction speech analysis.

## Layout
- `app/main.py`: App factory `create_app()`, routes, auth dependency, error handlers.
- `app/config.py`: Service configuration via `pydantic-settings`.
- `app/pipeline/audio.py`: ffprobe probing, ffmpeg normalization to 16 kHz mono PCM s16le WAV, PII muting to mono 32 kbps MP3.
- `app/pipeline/asr.py`: Transcriber protocol, faster-whisper backend (lazy model load, one call at a time in a worker thread), hallucination filter, stub.
- `app/pipeline/numbers.py`: Vietnamese spoken numbers to digits (`năm mươi triệu` → `50.000.000`, `bảy phẩy năm phần trăm` → `7,5%`), keeping source offsets.
- `app/pipeline/pii.py`: PII masking (`[STK]`, `[CCCD]`, `[SĐT]`, `[SỐ]`, `[EMAIL]`) and PII time spans (±150 ms, merged) from word timestamps. Money and rates are kept.
- `bench/asr_bench.py`: ASR model benchmark (dev only); results in `bench/RESULTS.md`.
- `app/schemas.py`, `app/errors.py`, `app/version.py`: Data models, errors, and versioning.

## Environment Variables
| Variable | Default | Description |
|---|---|---|
| `AI_SERVICE_KEY` | *(required)* | Shared secret key for API authentication (min 16 chars). |
| `MAX_AUDIO_SECONDS` | `1200` | Maximum allowed audio duration in seconds. |
| `MIN_AUDIO_SECONDS` | `3` | Minimum allowed audio duration in seconds. |
| `MAX_UPLOAD_BYTES` | `52428800` | Maximum upload file size in bytes (50 MB). |
| `FFMPEG_BIN` | `ffmpeg` | Path to ffmpeg executable. |
| `FFPROBE_BIN` | `ffprobe` | Path to ffprobe executable. |
| `FFMPEG_TIMEOUT_SECONDS` | `120` | Subprocess execution timeout in seconds. |
| `TMP_DIR` | `None` | Parent directory for temporary files (defaults to system temp). |
| `ASR_BACKEND` | `faster-whisper` | `faster-whisper` or `stub` (tests). |
| `ASR_MODEL` | `large-v3-turbo` | faster-whisper model name, Hugging Face CTranslate2 repo id or local path. |
| `ASR_DEVICE` | `auto` | `auto` (CUDA if available, else CPU), `cuda` or `cpu`. |
| `ASR_COMPUTE_TYPE` | `auto` | `auto` = `int8_float16` on CUDA, `int8` on CPU. |
| `ASR_MODEL_DIR` | `None` | Model download cache (defaults to the Hugging Face cache). |

## Local Development
```powershell
# Install dependencies
.venv\Scripts\python -m pip install -r ai-service\requirements-dev.txt

# Run service locally from ai-service directory
uvicorn --factory app.main:create_app --host 0.0.0.0 --port 8000 --reload

# Run tests (GPU smoke test: add -m gpu)
.venv\Scripts\python -m pytest ai-service -q -rs

# ASR benchmark (from ai-service, CUDA required)
..\.venv\Scripts\python -m bench.asr_bench --data-dir <folder with *.wav + *.timeline.json>
```

On Windows, CUDA needs `nvidia-cublas-cu12` and `nvidia-cudnn-cu12` (pip) with their `bin` folders on `PATH`.

## Endpoints
- `GET /health`: Health check (`200 {"status":"ok","pipeline_version":"0.2.0"}`). No auth.
- `POST /v1/analyze`: Multipart form (`audio`, `meta`). normalize → ASR → number normalisation → PII mask → PII mute. Returns stage `W2_MASKED`: masked `segments` only, `muted_audio {mime_type, base64}` (mono 32 kbps MP3), `stats.pii_masked`, `flags: []` (detectors come in AI-08).
- `POST /v1/detect`: JSON payload. Validates segment data and returns compliance flags and service spans.

### Error Codes
- `401`: `UNAUTHORIZED`
- `413`: `AUDIO_TOO_LARGE`
- `422`: `INVALID_REQUEST`, `INVALID_META`, `UNSUPPORTED_MEDIA`, `AUDIO_DECODE_FAILED`, `AUDIO_TOO_SHORT`, `AUDIO_TOO_LONG`
- `500`: `FFMPEG_NOT_AVAILABLE`, `AUDIO_MUTE_FAILED`, `INTERNAL_ERROR`
- `504`: `AUDIO_PROCESSING_TIMEOUT`

## Core Rules & Security
- **Audio is never committed**: No raw or normalized audio files are ever tracked in git.
- **Logs never contain transcript text**: Only structured identifiers, counts and processing timings are logged; client filenames and speech transcripts are never logged.
- **Unmasked text never leaves the pipeline**: raw ASR output exists only in memory; responses carry masked text and PII-muted audio.
- **No external AI services**: Analysis runs locally/offline; no external cloud AI APIs are called.

## Roadmap & Stubs
- S1-06: Skeleton, ffmpeg normalization, stub ASR.
- AI-03/AI-04 (current, `0.2.0`): faster-whisper ASR, number normalization, PII mask and mute.
- S1-10+: Compliance detection and Knowledge Base retrieval.
