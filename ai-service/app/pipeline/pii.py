"""PII masking on number-normalised transcripts, with the PII time spans used to mute the audio."""

from dataclasses import dataclass
import re
from typing import Sequence

from app.pipeline.asr import TranscriptSegment
from app.pipeline.numbers import NormalizedText, Piece, normalize
from app.schemas import Segment

PII_PAD_MS = 150
MIN_LONG_DIGITS = 8
MIN_ACCOUNT_DIGITS = 6
_CONTEXT_PIECES = 8
_ID_CONTEXT = ("căn cước", "cccd", "chứng minh", "cmnd", "cmt")
_ACCOUNT_CONTEXT = ("tài khoản", "stk", "số thẻ")
# A digit run followed by a currency word is an amount ("50000000 đồng"), not PII.
_MONEY_UNITS = frozenset({"đồng", "đ", "vnd", "vnđ"})
_TRAILING_PUNCT = ".,;:!?…\"')"
_DIGITS_RE = re.compile(r"\+?\d+(?:-\d+)*")
_PHONE_RE = re.compile(r"(?:0|84)(?:[35789]\d{8}|2\d{9})")
_EMAIL_RE = re.compile(r"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")


@dataclass(frozen=True)
class MaskedTranscript:
    segments: list[Segment]
    pii_spans: list[tuple[int, int]]
    pii_count: int


def mask_transcript(
    segments: Sequence[TranscriptSegment], duration_ms: int, pad_ms: int = PII_PAD_MS
) -> MaskedTranscript:
    """Normalise numbers, then replace PII with [STK], [CCCD], [SĐT], [SỐ] or [EMAIL].

    Money amounts and rates stay readable (needed by FACT_MISMATCH). Returns masked segments and the
    PII time spans from word timestamps, padded by `pad_ms`, clamped to the audio and merged.
    """
    masked: list[Segment] = []
    spans: list[tuple[int, int]] = []
    for index, segment in enumerate(segments):
        text, segment_spans = _mask_segment(segment)
        masked.append(Segment(id=index, start_ms=segment.start_ms, end_ms=segment.end_ms, text_masked=text))
        spans.extend(segment_spans)
    return MaskedTranscript(
        segments=masked,
        pii_spans=merge_spans(spans, pad_ms=pad_ms, duration_ms=duration_ms),
        pii_count=len(spans),
    )


def merge_spans(spans: Sequence[tuple[int, int]], *, pad_ms: int, duration_ms: int) -> list[tuple[int, int]]:
    merged: list[tuple[int, int]] = []
    for start, end in sorted((max(0, s - pad_ms), min(duration_ms, e + pad_ms)) for s, e in spans):
        if merged and start <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], end))
        else:
            merged.append((start, end))
    return merged


def _mask_segment(segment: TranscriptSegment) -> tuple[str, list[tuple[int, int]]]:
    normalized = normalize(segment.text)
    pieces = normalized.pieces
    out: list[str] = []
    spans: list[tuple[int, int]] = []
    i = 0
    while i < len(pieces):
        core = _core(pieces[i].text)
        if _EMAIL_RE.fullmatch(core):
            tag, end = "[EMAIL]", i + 1
        else:
            end = _digit_run_end(pieces, i)
            tag = _classify(pieces, i, end) if end > i else None
        if tag is None:
            out.append(pieces[i].text)
            i += 1
            continue
        last = pieces[end - 1].text
        out.append(tag + last[len(_core(last)) :])
        spans.append(_time_span(segment, normalized, pieces[i], pieces[end - 1]))
        i = end
    return " ".join(out), spans


def _core(text: str) -> str:
    return text.rstrip(_TRAILING_PUNCT)


def _digit_run_end(pieces: Sequence[Piece], i: int) -> int:
    """Index after a run of digit pieces ("0912 345 678"); a run continues across commas only."""
    j = i
    while j < len(pieces) and _DIGITS_RE.fullmatch(_core(pieces[j].text)):
        j += 1
        if pieces[j - 1].text[len(_core(pieces[j - 1].text)) :] not in ("", ","):
            break
    return j


def _classify(pieces: Sequence[Piece], start: int, end: int) -> str | None:
    digits = re.sub(r"\D", "", "".join(p.text for p in pieces[start:end]))
    following = _core(pieces[end].text).lower() if end < len(pieces) else ""
    if following in _MONEY_UNITS:
        return None
    context = " ".join(p.text for p in pieces[max(0, start - _CONTEXT_PIECES) : start]).lower()
    if any(word in context for word in _ID_CONTEXT) and len(digits) in (9, 12):
        return "[CCCD]"
    if any(word in context for word in _ACCOUNT_CONTEXT) and len(digits) >= MIN_ACCOUNT_DIGITS:
        return "[STK]"
    if _PHONE_RE.fullmatch(digits):
        return "[SĐT]"
    if len(digits) == 12:
        return "[CCCD]"
    if len(digits) >= MIN_LONG_DIGITS:
        return "[SỐ]"
    return None


def _time_span(segment: TranscriptSegment, normalized: NormalizedText, first: Piece, last: Piece) -> tuple[int, int]:
    source_start, source_end = normalized.source_range(first.start, last.end)
    words = []
    offset = 0
    for word in segment.words:
        if offset < source_end and source_start < offset + len(word.text):
            words.append(word)
        offset += len(word.text) + 1
    return words[0].start_ms, words[-1].end_ms
