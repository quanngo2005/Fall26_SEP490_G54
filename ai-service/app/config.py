"""Application configuration settings."""

from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="")

    AI_SERVICE_KEY: str = Field(..., min_length=16)
    MAX_AUDIO_SECONDS: float = 1200.0
    MIN_AUDIO_SECONDS: float = 3.0
    MAX_UPLOAD_BYTES: int = 52428800
    FFMPEG_BIN: str = "ffmpeg"
    FFPROBE_BIN: str = "ffprobe"
    FFMPEG_TIMEOUT_SECONDS: int = 120
    TMP_DIR: Path | None = None
