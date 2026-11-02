# AI Service (`ai-service/`)

FastAPI microservice for audio normalization (16 kHz mono WAV) and transaction speech analysis.

## Layout
- `app/main.py`: App factory `create_app()`, routes, auth dependency, error handlers.
- `app/config.py`: Service configuration via `pydantic-settings`.
- `app/pipeline/audio.py`: ffprobe probing and ffmpeg normalization to 16 kHz mono PCM s16le WAV.
- `app/pipeline/asr.py`: Transcriber protocol and stub implementation.
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

## Local Development
```powershell
# Install dependencies
.venv\Scripts\python -m pip install -r ai-service\requirements-dev.txt

# Run service locally from ai-service directory
uvicorn --factory app.main:create_app --host 0.0.0.0 --port 8000 --reload

# Run tests
.venv\Scripts\python -m pytest ai-service -q -rs
```

## Endpoints
- `GET /health`: Health check (`200 {"status":"ok","pipeline_version":"0.1.0"}`). No auth.
- `POST /v1/analyze`: Multipart form (`audio`, `meta`). Normalizes audio and returns transcription segments and metadata.
- `POST /v1/detect`: JSON payload. Validates segment data and returns compliance flags and service spans.

### Error Codes
- `401`: `UNAUTHORIZED`
- `413`: `AUDIO_TOO_LARGE`
- `422`: `INVALID_REQUEST`, `INVALID_META`, `UNSUPPORTED_MEDIA`, `AUDIO_DECODE_FAILED`, `AUDIO_TOO_SHORT`, `AUDIO_TOO_LONG`
- `500`: `FFMPEG_NOT_AVAILABLE`, `INTERNAL_ERROR`
- `504`: `AUDIO_PROCESSING_TIMEOUT`

## Core Rules & Security
- **Audio is never committed**: No raw or normalized audio files are ever tracked in git.
- **Logs never contain transcript text**: Only structured identifiers and processing timings are logged; client filenames and speech transcripts are never logged.
- **No external AI services**: Analysis runs locally/offline; no external cloud AI APIs are called.

## Roadmap & Stubs
- Current state (S1-06): Skeleton, ffmpeg normalization, stub ASR.
- S1-08: Real ASR with faster-whisper.
- S1-09: Number normalization and PII masking.
- S1-10+: Compliance detection and Knowledge Base retrieval.
