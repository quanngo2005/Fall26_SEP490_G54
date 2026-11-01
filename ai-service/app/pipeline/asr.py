"""ASR transcription interfaces and stub implementation."""

from pathlib import Path
from typing import Protocol

from app.schemas import Segment, Speaker


class Transcriber(Protocol):
    model_name: str

    def transcribe(self, wav_path: Path, duration_ms: int) -> list[Segment]:
        ...


class StubTranscriber:
    model_name = "stub"

    def transcribe(self, wav_path: Path, duration_ms: int) -> list[Segment]:
        return [
            Segment(
                id=0,
                start_ms=0,
                end_ms=duration_ms,
                speaker=Speaker.UNKNOWN,
                text_masked="",
                service_code=None,
            )
        ]
