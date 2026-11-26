"""Tests for /v1/analyze endpoint."""

import base64
import hashlib
import json
import logging
from pathlib import Path
from uuid import uuid4

from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from app.pipeline.asr import SerialTranscriber, TranscriptSegment, Word
from app.version import PIPELINE_VERSION


def test_analyze_success_mono_wav(client: TestClient, auth_headers: dict[str, str], make_tone) -> None:
    audio_path = make_tone(10.0, 8000, 1, "wav")
    file_bytes = audio_path.read_bytes()
    expected_sha256 = hashlib.sha256(file_bytes).hexdigest().lower()

    rec_id = str(uuid4())
    meta = json.dumps({"recording_id": rec_id, "product_code": "SAVINGS"})

    with open(audio_path, "rb") as f:
        resp = client.post(
            "/v1/analyze",
            headers=auth_headers,
            files={"audio": ("sample.wav", f, "audio/wav")},
            data={"meta": meta},
        )

    assert resp.status_code == 200
    data = resp.json()
    assert data["pipeline_version"] == PIPELINE_VERSION
    assert data["stage"] == "W2_MASKED"
    assert len(data["segments"]) >= 1
    for seg in data["segments"]:
        assert seg["start_ms"] < seg["end_ms"]
        assert seg["speaker"] == "UNKNOWN"
    assert data["asr"]["channels"] == 1
    assert data["stats"]["source_sample_rate"] == 8000
    assert data["stats"]["source_channels"] == 1
    assert data["stats"]["source_sha256"] == expected_sha256
    assert data["flags"] == []
    assert data["stats"]["pii_masked"] == 0
    assert data["muted_audio"]["mime_type"] == "audio/mpeg"
    assert base64.b64decode(data["muted_audio"]["base64"])
    assert "service_spans" not in data
    assert "kb_version" not in data


def test_analyze_success_stereo_m4a(client: TestClient, auth_headers: dict[str, str], make_tone) -> None:
    audio_path = make_tone(10.0, 48000, 2, "m4a", ["-c:a", "aac"])

    rec_id = str(uuid4())
    meta = json.dumps({"recording_id": rec_id})

    with open(audio_path, "rb") as f:
        resp = client.post(
            "/v1/analyze",
            headers=auth_headers,
            files={"audio": ("stereo.m4a", f, "audio/mp4")},
            data={"meta": meta},
        )

    assert resp.status_code == 200
    data = resp.json()
    assert data["stats"]["source_channels"] == 2
    assert data["asr"]["channels"] == 1


def test_analyze_unsupported_media(client: TestClient, auth_headers: dict[str, str], tmp_path: Path) -> None:
    fake_audio = tmp_path / "corrupt.wav"
    fake_audio.write_text("not audio data", encoding="utf-8")

    meta = json.dumps({"recording_id": str(uuid4())})

    with open(fake_audio, "rb") as f:
        resp = client.post(
            "/v1/analyze",
            headers=auth_headers,
            files={"audio": ("corrupt.wav", f, "audio/wav")},
            data={"meta": meta},
        )

    assert resp.status_code == 422
    assert resp.json()["error_code"] == "UNSUPPORTED_MEDIA"
    assert set(resp.json().keys()) == {"error_code", "message"}


def test_analyze_duration_limits(test_key: str, make_tone) -> None:
    custom_settings = Settings(
        AI_SERVICE_KEY=test_key,
        MIN_AUDIO_SECONDS=3.0,
        MAX_AUDIO_SECONDS=5.0,
    )
    custom_client = TestClient(create_app(custom_settings))
    headers = {"X-Service-Key": test_key}

    # 1. Audio too long (6 seconds > 5.0)
    long_audio = make_tone(6.0, 8000, 1, "wav")
    meta = json.dumps({"recording_id": str(uuid4())})
    with open(long_audio, "rb") as f:
        resp_long = custom_client.post(
            "/v1/analyze",
            headers=headers,
            files={"audio": ("long.wav", f, "audio/wav")},
            data={"meta": meta},
        )
    assert resp_long.status_code == 422
    assert resp_long.json()["error_code"] == "AUDIO_TOO_LONG"
    assert set(resp_long.json().keys()) == {"error_code", "message"}

    # 2. Audio too short (1 second < 3.0)
    short_audio = make_tone(1.0, 8000, 1, "wav")
    with open(short_audio, "rb") as f:
        resp_short = custom_client.post(
            "/v1/analyze",
            headers=headers,
            files={"audio": ("short.wav", f, "audio/wav")},
            data={"meta": meta},
        )
    assert resp_short.status_code == 422
    assert resp_short.json()["error_code"] == "AUDIO_TOO_SHORT"
    assert set(resp_short.json().keys()) == {"error_code", "message"}


def test_analyze_upload_too_large(test_key: str, make_tone) -> None:
    custom_settings = Settings(
        AI_SERVICE_KEY=test_key,
        MAX_UPLOAD_BYTES=1000,  # 1 KB limit
    )
    custom_client = TestClient(create_app(custom_settings))
    headers = {"X-Service-Key": test_key}

    audio_path = make_tone(4.0, 8000, 1, "wav")
    meta = json.dumps({"recording_id": str(uuid4())})

    with open(audio_path, "rb") as f:
        resp = custom_client.post(
            "/v1/analyze",
            headers=headers,
            files={"audio": ("test.wav", f, "audio/wav")},
            data={"meta": meta},
        )

    assert resp.status_code == 413
    assert resp.json()["error_code"] == "AUDIO_TOO_LARGE"
    assert set(resp.json().keys()) == {"error_code", "message"}


def test_analyze_bad_meta(client: TestClient, auth_headers: dict[str, str], make_tone) -> None:
    audio_path = make_tone(4.0, 8000, 1, "wav")

    # 1. Not JSON
    with open(audio_path, "rb") as f:
        resp = client.post(
            "/v1/analyze",
            headers=auth_headers,
            files={"audio": ("test.wav", f, "audio/wav")},
            data={"meta": "not-valid-json"},
        )
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "INVALID_META"
    assert set(resp.json().keys()) == {"error_code", "message"}

    # 2. Missing recording_id
    with open(audio_path, "rb") as f:
        resp = client.post(
            "/v1/analyze",
            headers=auth_headers,
            files={"audio": ("test.wav", f, "audio/wav")},
            data={"meta": json.dumps({"product_code": "PROD1"})},
        )
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "INVALID_META"
    assert set(resp.json().keys()) == {"error_code", "message"}

    # 3. Extra teller_channel (forbidden extra field)
    with open(audio_path, "rb") as f:
        resp = client.post(
            "/v1/analyze",
            headers=auth_headers,
            files={"audio": ("test.wav", f, "audio/wav")},
            data={"meta": json.dumps({"recording_id": str(uuid4()), "teller_channel": 1})},
        )
    assert resp.status_code == 422
    assert resp.json()["error_code"] == "INVALID_META"
    assert set(resp.json().keys()) == {"error_code", "message"}


def test_analyze_temp_cleanup(test_key: str, tmp_path: Path, make_tone) -> None:
    work_dir = tmp_path / "work"
    custom_settings = Settings(
        AI_SERVICE_KEY=test_key,
        TMP_DIR=work_dir,
    )
    custom_client = TestClient(create_app(custom_settings))
    headers = {"X-Service-Key": test_key}

    # 1. Successful request
    good_audio = make_tone(4.0, 8000, 1, "wav")
    meta = json.dumps({"recording_id": str(uuid4())})
    with open(good_audio, "rb") as f:
        resp_good = custom_client.post(
            "/v1/analyze",
            headers=headers,
            files={"audio": ("good.wav", f, "audio/wav")},
            data={"meta": meta},
        )
    assert resp_good.status_code == 200
    assert list(work_dir.iterdir()) == []

    # 2. Failing request (corrupt audio)
    bad_audio = tmp_path / "bad.wav"
    bad_audio.write_text("corrupted content", encoding="utf-8")
    with open(bad_audio, "rb") as f:
        resp_bad = custom_client.post(
            "/v1/analyze",
            headers=headers,
            files={"audio": ("bad.wav", f, "audio/wav")},
            data={"meta": meta},
        )
    assert resp_bad.status_code == 422
    assert list(work_dir.iterdir()) == []


class _PiiTranscriber:
    model_name = "fake"

    def transcribe(self, wav_path: Path, duration_ms: int) -> list[TranscriptSegment]:
        spoken = "số tài khoản là một chín không ba bốn năm sáu bảy tám, lãi năm phẩy hai phần trăm"
        words = tuple(Word(text=w, start_ms=i * 200, end_ms=i * 200 + 180) for i, w in enumerate(spoken.split()))
        return [TranscriptSegment(start_ms=0, end_ms=words[-1].end_ms, words=words)]


def test_analyze_masks_pii_and_mutes_audio(test_key: str, make_tone, caplog) -> None:
    app = create_app(Settings(AI_SERVICE_KEY=test_key))
    app.state.transcriber = SerialTranscriber(_PiiTranscriber())
    audio_path = make_tone(5.0, 8000, 1, "wav")

    with caplog.at_level(logging.DEBUG), open(audio_path, "rb") as f:
        resp = TestClient(app).post(
            "/v1/analyze",
            headers={"X-Service-Key": test_key},
            files={"audio": ("customer.wav", f, "audio/wav")},
            data={"meta": json.dumps({"recording_id": str(uuid4())})},
        )

    assert resp.status_code == 200
    data = resp.json()
    assert [s["text_masked"] for s in data["segments"]] == ["số tài khoản là [STK], lãi 5,2%"]
    assert data["stats"]["pii_masked"] == 1
    assert data["muted_audio"]["mime_type"] == "audio/mpeg"
    assert base64.b64decode(data["muted_audio"]["base64"])[:3] in (b"ID3", b"\xff\xfb", b"\xff\xf3")
    body = resp.text + caplog.text
    assert "190345678" not in body and "một chín" not in body
    assert "customer.wav" not in caplog.text
