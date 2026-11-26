"""Tests for transcriber selection, call serialisation and the hallucination filter."""

import asyncio
from pathlib import Path
import threading
import time
from types import SimpleNamespace

import pytest

from app.config import Settings
from app.pipeline.asr import (
    FasterWhisperTranscriber,
    SerialTranscriber,
    StubTranscriber,
    TranscriptSegment,
    build_transcriber,
    filter_hallucinations,
)


def _whisper_segment(text: str, start: float, end: float, no_speech_prob: float = 0.1, avg_logprob: float = -0.3):
    tokens = text.split()
    step = (end - start) / len(tokens)
    words = [
        SimpleNamespace(word=f" {w}", start=start + i * step, end=start + (i + 1) * step) for i, w in enumerate(tokens)
    ]
    return SimpleNamespace(start=start, end=end, words=words, no_speech_prob=no_speech_prob, avg_logprob=avg_logprob)


def test_build_transcriber_selects_backend(test_key: str) -> None:
    assert isinstance(build_transcriber(Settings(AI_SERVICE_KEY=test_key)), StubTranscriber)

    real = build_transcriber(
        Settings(AI_SERVICE_KEY=test_key, ASR_BACKEND="faster-whisper", ASR_DEVICE="cpu", ASR_MODEL="large-v3-turbo")
    )
    assert isinstance(real, FasterWhisperTranscriber)
    assert real.model_name == "large-v3-turbo-int8"


def test_serial_transcriber_runs_one_call_at_a_time() -> None:
    class _Slow:
        model_name = "slow"
        active = 0
        peak = 0
        guard = threading.Lock()

        def transcribe(self, wav_path: Path, duration_ms: int) -> list[TranscriptSegment]:
            with self.guard:
                self.active += 1
                self.peak = max(self.peak, self.active)
            time.sleep(0.05)
            with self.guard:
                self.active -= 1
            return []

    inner = _Slow()
    serial = SerialTranscriber(inner)

    async def _run() -> None:
        await asyncio.gather(*(serial.transcribe(Path("x.wav"), 1_000) for _ in range(4)))

    asyncio.run(_run())
    assert inner.peak == 1
    assert serial.model_name == "slow"


def test_filter_hallucinations() -> None:
    segments = [
        _whisper_segment("dạ em chào chị", 0.0, 2.0),
        _whisper_segment("cảm ơn các bạn đã xem", 2.0, 4.0, no_speech_prob=0.9, avg_logprob=-1.5),
        _whisper_segment("ừ ừ ừ ừ ừ", 4.0, 5.0),
        _whisper_segment("đăng ký kênh", 5.0, 6.0),
        _whisper_segment("đăng ký kênh", 6.0, 7.0),
        _whisper_segment("đăng ký kênh", 7.0, 8.0),
        _whisper_segment("vâng ạ", 8.0, 9.0),
        _whisper_segment("vâng ạ", 9.0, 9.8),
        _whisper_segment("hẹn gặp lại", 10.5, 12.0),
    ]

    result = filter_hallucinations(segments, duration_ms=10_000)

    assert [s.text for s in result] == ["dạ em chào chị", "đăng ký kênh", "vâng ạ", "vâng ạ"]
    assert result[0].words[0].text == "dạ"
    assert (result[0].start_ms, result[0].end_ms) == (0, 2_000)


def test_filter_keeps_short_legit_repeats() -> None:
    result = filter_hallucinations([_whisper_segment("dạ dạ dạ vâng", 0.0, 2.0)], duration_ms=5_000)

    assert [s.text for s in result] == ["dạ dạ dạ vâng"]


@pytest.mark.gpu
def test_faster_whisper_gpu_smoke(make_tone) -> None:
    wav = make_tone(5.0, 16000, 1, "wav")
    transcriber = FasterWhisperTranscriber("large-v3-turbo", device="cuda", compute_type="int8_float16")

    segments = transcriber.transcribe(wav, 5_000)

    assert all(0 <= s.start_ms < s.end_ms <= 5_000 for s in segments)
