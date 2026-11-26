"""Audio probe, normalization and PII muting utilities."""

from dataclasses import dataclass
import hashlib
import json
import logging
from pathlib import Path
import subprocess
from typing import Sequence

from app.errors import AudioError

logger = logging.getLogger(__name__)

MUTED_AUDIO_MIME_TYPE = "audio/mpeg"


@dataclass(frozen=True)
class AudioInfo:
    duration_ms: int
    sample_rate: int
    channels: int
    codec: str | None
    format_name: str | None


def _run_tool(cmd: list[str], *, tool: str, timeout_s: int) -> subprocess.CompletedProcess[bytes]:
    try:
        res = subprocess.run(cmd, capture_output=True, timeout=timeout_s, check=False)
    except subprocess.TimeoutExpired as exc:
        raise AudioError(
            code="AUDIO_PROCESSING_TIMEOUT",
            http_status=504,
            message="Audio processing timed out",
        ) from exc
    except FileNotFoundError as exc:
        raise AudioError(
            code="FFMPEG_NOT_AVAILABLE",
            http_status=500,
            message=f"{tool} binary not found",
        ) from exc

    if res.returncode != 0 and res.stderr:
        logger.debug("%s stderr: %s", tool, res.stderr[:200].decode("utf-8", errors="replace"))
    return res


def probe(path: Path, *, ffprobe_bin: str = "ffprobe", timeout_s: int = 30) -> AudioInfo:
    cmd = [
        ffprobe_bin,
        "-v",
        "error",
        "-select_streams",
        "a:0",
        "-show_entries",
        "format=duration,format_name:stream=codec_name,sample_rate,channels,duration",
        "-of",
        "json",
        str(path),
    ]

    res = _run_tool(cmd, tool="ffprobe", timeout_s=timeout_s)
    if res.returncode != 0:
        raise AudioError(
            code="UNSUPPORTED_MEDIA",
            http_status=422,
            message="Unsupported media format or corrupt file",
        )

    try:
        data = json.loads(res.stdout)
    except Exception as exc:
        raise AudioError(
            code="UNSUPPORTED_MEDIA",
            http_status=422,
            message="Could not parse ffprobe output",
        ) from exc

    streams = data.get("streams", [])
    if not streams:
        raise AudioError(
            code="UNSUPPORTED_MEDIA",
            http_status=422,
            message="No audio stream found in file",
        )

    stream = streams[0]
    fmt = data.get("format", {})

    raw_duration = fmt.get("duration") if fmt.get("duration") is not None else stream.get("duration")
    if raw_duration is None:
        raise AudioError(
            code="UNSUPPORTED_MEDIA",
            http_status=422,
            message="Audio duration not available",
        )

    try:
        duration_seconds = float(raw_duration)
        if duration_seconds <= 0:
            raise ValueError("Duration must be positive")
    except (ValueError, TypeError) as exc:
        raise AudioError(
            code="UNSUPPORTED_MEDIA",
            http_status=422,
            message="Invalid audio duration",
        ) from exc

    duration_ms = round(duration_seconds * 1000)

    try:
        sample_rate = int(stream["sample_rate"])
        channels = int(stream["channels"])
    except (KeyError, ValueError, TypeError) as exc:
        raise AudioError(
            code="UNSUPPORTED_MEDIA",
            http_status=422,
            message="Invalid audio stream properties",
        ) from exc

    codec = stream.get("codec_name")
    format_name = fmt.get("format_name")

    return AudioInfo(
        duration_ms=duration_ms,
        sample_rate=sample_rate,
        channels=channels,
        codec=codec,
        format_name=format_name,
    )


def normalize(
    src: Path,
    dst: Path,
    *,
    ffmpeg_bin: str = "ffmpeg",
    ffprobe_bin: str = "ffprobe",
    timeout_s: int = 120,
) -> AudioInfo:
    cmd = [
        ffmpeg_bin,
        "-nostdin",
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-i",
        str(src),
        "-vn",
        "-ac",
        "1",
        "-ar",
        "16000",
        "-acodec",
        "pcm_s16le",
        "-f",
        "wav",
        str(dst),
    ]

    res = _run_tool(cmd, tool="ffmpeg", timeout_s=timeout_s)
    if res.returncode != 0:
        raise AudioError(
            code="AUDIO_DECODE_FAILED",
            http_status=422,
            message="Audio decoding and normalization failed",
        )

    return probe(dst, ffprobe_bin=ffprobe_bin, timeout_s=timeout_s)


def build_mute_command(
    src: Path,
    dst: Path,
    spans: Sequence[tuple[int, int]],
    *,
    ffmpeg_bin: str = "ffmpeg",
) -> list[str]:
    cmd = [ffmpeg_bin, "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", str(src)]
    if spans:
        filters = (f"volume=enable='between(t,{start / 1000:.3f},{end / 1000:.3f})':volume=0" for start, end in spans)
        cmd.extend(["-af", ",".join(filters)])
    cmd.extend(["-vn", "-ac", "1", "-c:a", "libmp3lame", "-b:a", "32k", str(dst)])
    return cmd


def mute_spans(
    src: Path,
    dst: Path,
    spans: Sequence[tuple[int, int]],
    *,
    ffmpeg_bin: str = "ffmpeg",
    timeout_s: int = 120,
) -> Path:
    """Silence the given millisecond spans and encode a mono 32 kbps MP3 (contract `muted_audio`)."""
    res = _run_tool(build_mute_command(src, dst, spans, ffmpeg_bin=ffmpeg_bin), tool="ffmpeg", timeout_s=timeout_s)
    if res.returncode != 0:
        raise AudioError(
            code="AUDIO_MUTE_FAILED",
            http_status=500,
            message="Muting PII in audio failed",
        )
    return dst


def sha256_file(path: Path, chunk_size: int = 1 << 20) -> str:
    hasher = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            chunk = f.read(chunk_size)
            if not chunk:
                break
            hasher.update(chunk)
    return hasher.hexdigest().lower()


def check_duration(info: AudioInfo, *, min_seconds: float, max_seconds: float) -> None:
    duration_s = info.duration_ms / 1000.0
    if duration_s < min_seconds:
        raise AudioError(
            code="AUDIO_TOO_SHORT",
            http_status=422,
            message="Audio duration is too short",
        )
    if duration_s > max_seconds:
        raise AudioError(
            code="AUDIO_TOO_LONG",
            http_status=422,
            message="Audio duration exceeds maximum limit",
        )
