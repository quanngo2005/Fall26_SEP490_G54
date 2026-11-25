"""Pytest fixtures and configuration."""

from pathlib import Path
import shutil
import subprocess

from fastapi.testclient import TestClient
import pytest

from app.config import Settings
from app.main import create_app

TEST_SERVICE_KEY = "test-key-0123456789"


@pytest.fixture
def test_key() -> str:
    return TEST_SERVICE_KEY


@pytest.fixture
def default_settings() -> Settings:
    return Settings(AI_SERVICE_KEY=TEST_SERVICE_KEY)


@pytest.fixture
def client(default_settings: Settings) -> TestClient:
    app = create_app(default_settings)
    return TestClient(app)


@pytest.fixture
def auth_headers(default_settings: Settings) -> dict[str, str]:
    return {"X-Service-Key": default_settings.AI_SERVICE_KEY}


@pytest.fixture
def make_tone(tmp_path: Path):
    if shutil.which("ffmpeg") is None:
        pytest.skip("ffmpeg not found")

    counter = 0

    def _make(
        seconds: float,
        sample_rate: int,
        channels: int,
        ext: str,
        codec_args: list[str] | None = None,
    ) -> Path:
        nonlocal counter
        counter += 1
        suffix = ext if ext.startswith(".") else f".{ext}"
        out_path = tmp_path / f"tone_{counter}{suffix}"

        cmd = [
            "ffmpeg",
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-f",
            "lavfi",
            "-i",
            f"sine=frequency=440:sample_rate={sample_rate}:duration={seconds}",
            "-ac",
            str(channels),
        ]
        if codec_args:
            cmd.extend(codec_args)
        cmd.append(str(out_path))

        res = subprocess.run(cmd, capture_output=True, check=False)
        if res.returncode != 0:
            err = res.stderr.decode("utf-8", errors="replace")
            if "libopencore_amrnb" in str(codec_args) and ("Unknown encoder" in err or "unsupported" in err.lower()):
                pytest.skip("AMR encoder libopencore_amrnb not available")
            raise RuntimeError(f"ffmpeg failed generating tone: {err}")

        return out_path

    return _make
