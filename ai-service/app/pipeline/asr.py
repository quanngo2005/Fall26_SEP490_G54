"""ASR transcription: transcriber protocol, faster-whisper backend and hallucination filter."""

import asyncio
from dataclasses import dataclass
from itertools import groupby
from pathlib import Path
from typing import Any, Iterable, Protocol, Sequence

from app.config import Settings

# Domain terms passed to Whisper as `initial_prompt`; the knowledge base will supply this list later.
ASR_GLOSSARY: tuple[str, ...] = (
    "tiết kiệm",
    "kỳ hạn",
    "lãi suất",
    "phí thường niên",
    "thẻ tín dụng",
    "thẻ ghi nợ",
    "tài khoản thanh toán",
    "bảo hiểm liên kết",
    "giải ngân",
    "tất toán",
    "e-banking",
    "OTP",
)
NO_SPEECH_PROB_MAX = 0.6
AVG_LOGPROB_MIN = -1.0
MAX_NGRAM = 4
MAX_NGRAM_REPEATS = 3
MAX_SEGMENT_REPEATS = 3


@dataclass(frozen=True)
class Word:
    text: str
    start_ms: int
    end_ms: int


@dataclass(frozen=True)
class TranscriptSegment:
    """Raw (unmasked) ASR output; never leaves the service."""

    start_ms: int
    end_ms: int
    words: tuple[Word, ...]

    @property
    def text(self) -> str:
        return " ".join(w.text for w in self.words)


class Transcriber(Protocol):
    model_name: str

    def transcribe(self, wav_path: Path, duration_ms: int) -> list[TranscriptSegment]:
        ...


class StubTranscriber:
    model_name = "stub"

    def transcribe(self, wav_path: Path, duration_ms: int) -> list[TranscriptSegment]:
        return [TranscriptSegment(start_ms=0, end_ms=duration_ms, words=())]


class FasterWhisperTranscriber:
    """faster-whisper backend; the model is loaded on the first call."""

    def __init__(
        self,
        model: str,
        *,
        device: str,
        compute_type: str,
        model_dir: Path | None = None,
        glossary: Sequence[str] = ASR_GLOSSARY,
    ) -> None:
        self.model_name = f"{model}-{compute_type}"
        self._model_ref = model
        self._device = device
        self._compute_type = compute_type
        self._model_dir = model_dir
        self._initial_prompt = ", ".join(glossary) or None
        self._model: Any = None

    def transcribe(self, wav_path: Path, duration_ms: int) -> list[TranscriptSegment]:
        if self._model is None:
            from faster_whisper import WhisperModel

            self._model = WhisperModel(
                self._model_ref,
                device=self._device,
                compute_type=self._compute_type,
                download_root=str(self._model_dir) if self._model_dir else None,
            )
        segments, _ = self._model.transcribe(
            str(wav_path),
            language="vi",
            vad_filter=True,
            word_timestamps=True,
            initial_prompt=self._initial_prompt,
            condition_on_previous_text=False,
        )
        return filter_hallucinations(segments, duration_ms)


class SerialTranscriber:
    """Runs a blocking transcriber in a worker thread, one call at a time (one model on one GPU)."""

    def __init__(self, inner: Transcriber) -> None:
        self._inner = inner
        self._lock = asyncio.Lock()

    @property
    def model_name(self) -> str:
        return self._inner.model_name

    async def transcribe(self, wav_path: Path, duration_ms: int) -> list[TranscriptSegment]:
        async with self._lock:
            return await asyncio.to_thread(self._inner.transcribe, wav_path, duration_ms)


def build_transcriber(settings: Settings) -> Transcriber:
    if settings.ASR_BACKEND == "stub":
        return StubTranscriber()
    device = settings.ASR_DEVICE
    if device == "auto":
        import ctranslate2

        device = "cuda" if ctranslate2.get_cuda_device_count() > 0 else "cpu"
    compute_type = settings.ASR_COMPUTE_TYPE
    if compute_type == "auto":
        compute_type = "int8_float16" if device == "cuda" else "int8"
    return FasterWhisperTranscriber(
        settings.ASR_MODEL,
        device=device,
        compute_type=compute_type,
        model_dir=settings.ASR_MODEL_DIR,
    )


def filter_hallucinations(segments: Iterable[Any], duration_ms: int) -> list[TranscriptSegment]:
    """Convert faster-whisper segments, dropping typical Whisper hallucinations.

    Dropped: silence decoded as text (high `no_speech_prob` with low `avg_logprob`), words past the
    audio end, segments with an n-gram repeated more than MAX_NGRAM_REPEATS times in a row, and
    repeats of a segment said MAX_SEGMENT_REPEATS or more times in a row (the first one is kept).
    """
    kept: list[TranscriptSegment] = []
    for seg in segments:
        if seg.no_speech_prob > NO_SPEECH_PROB_MAX and seg.avg_logprob < AVG_LOGPROB_MIN:
            continue
        words = tuple(
            Word(text=w.word.strip(), start_ms=round(w.start * 1000), end_ms=min(round(w.end * 1000), duration_ms))
            for w in seg.words or ()
            if w.word.strip() and round(w.start * 1000) < duration_ms
        )
        start_ms = max(0, round(seg.start * 1000))
        end_ms = min(round(seg.end * 1000), duration_ms)
        if not words or end_ms <= start_ms or _has_repeated_ngram([w.text.lower() for w in words]):
            continue
        kept.append(TranscriptSegment(start_ms=start_ms, end_ms=end_ms, words=words))

    result: list[TranscriptSegment] = []
    for _, group in groupby(kept, key=lambda s: s.text.lower()):
        run = list(group)
        result.extend(run[:1] if len(run) >= MAX_SEGMENT_REPEATS else run)
    return result


def _has_repeated_ngram(tokens: list[str]) -> bool:
    for n in range(1, MAX_NGRAM + 1):
        for i in range(len(tokens) - n):
            repeats = 1
            while tokens[i + repeats * n : i + (repeats + 1) * n] == tokens[i : i + n]:
                repeats += 1
            if repeats > MAX_NGRAM_REPEATS:
                return True
    return False
