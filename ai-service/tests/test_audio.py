"""Tests for audio probing, normalization, hashing, and duration checks."""

import hashlib
from pathlib import Path
import re
import subprocess

import pytest

from app.errors import AudioError
from app.pipeline.audio import (
    AudioInfo,
    build_mute_command,
    check_duration,
    mute_spans,
    normalize,
    probe,
    sha256_file,
)


@pytest.mark.parametrize(
    "sample_rate,channels,ext,codec_args",
    [
        (8000, 1, "wav", None),
        (48000, 2, "wav", None),
        (44100, 2, "mp3", ["-c:a", "libmp3lame"]),
        (22050, 1, "m4a", ["-c:a", "aac"]),
        (8000, 1, "amr", ["-c:a", "libopencore_amrnb", "-ar", "8000", "-ac", "1", "-b:a", "12.2k"]),
    ],
)
def test_audio_normalize_parametrized(
    make_tone,
    tmp_path: Path,
    sample_rate: int,
    channels: int,
    ext: str,
    codec_args: list[str] | None,
) -> None:
    # 4 second tone
    src = make_tone(4.0, sample_rate, channels, ext, codec_args)
    in_probe = probe(src)

    dst = tmp_path / f"normalized_{ext}.wav"
    out_probe = normalize(src, dst)

    assert out_probe.sample_rate == 16000
    assert out_probe.channels == 1
    assert out_probe.codec == "pcm_s16le"
    assert abs(out_probe.duration_ms - in_probe.duration_ms) <= 150


def test_audio_probe_properties(make_tone, tmp_path: Path) -> None:
    # 8 kHz mono
    mono_src = make_tone(2.0, 8000, 1, "wav")
    mono_info = probe(mono_src)
    assert mono_info.sample_rate == 8000
    assert mono_info.channels == 1
    assert mono_info.duration_ms > 0

    # 48 kHz stereo
    stereo_src = make_tone(2.0, 48000, 2, "wav")
    stereo_info = probe(stereo_src)
    assert stereo_info.sample_rate == 48000
    assert stereo_info.channels == 2
    assert stereo_info.duration_ms > 0

    # Text file probe -> UNSUPPORTED_MEDIA
    text_file = tmp_path / "not_audio.wav"
    text_file.write_text("This is not an audio file", encoding="utf-8")

    with pytest.raises(AudioError) as exc_info:
        probe(text_file)
    assert exc_info.value.code == "UNSUPPORTED_MEDIA"
    assert exc_info.value.http_status == 422


def test_sha256_file(tmp_path: Path) -> None:
    test_file = tmp_path / "test_data.bin"
    content = b"arbitrary-audio-payload-content-12345"
    test_file.write_bytes(content)

    expected = hashlib.sha256(content).hexdigest().lower()
    actual = sha256_file(test_file)
    assert actual == expected


def test_check_duration() -> None:
    info_short = AudioInfo(
        duration_ms=2000,
        sample_rate=16000,
        channels=1,
        codec="pcm_s16le",
        format_name="wav",
    )
    with pytest.raises(AudioError) as exc_short:
        check_duration(info_short, min_seconds=3.0, max_seconds=10.0)
    assert exc_short.value.code == "AUDIO_TOO_SHORT"
    assert exc_short.value.http_status == 422

    info_long = AudioInfo(
        duration_ms=12000,
        sample_rate=16000,
        channels=1,
        codec="pcm_s16le",
        format_name="wav",
    )
    with pytest.raises(AudioError) as exc_long:
        check_duration(info_long, min_seconds=3.0, max_seconds=10.0)
    assert exc_long.value.code == "AUDIO_TOO_LONG"
    assert exc_long.value.http_status == 422

    info_ok = AudioInfo(
        duration_ms=5000,
        sample_rate=16000,
        channels=1,
        codec="pcm_s16le",
        format_name="wav",
    )
    # Should not raise
    check_duration(info_ok, min_seconds=3.0, max_seconds=10.0)


def test_build_mute_command() -> None:
    cmd = build_mute_command(Path("in.wav"), Path("out.mp3"), [(1_000, 2_500), (4_000, 4_150)], ffmpeg_bin="ff")

    assert cmd[:8] == ["ff", "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", "in.wav"]
    assert cmd[cmd.index("-af") + 1] == (
        "volume=enable='between(t,1.000,2.500)':volume=0,volume=enable='between(t,4.000,4.150)':volume=0"
    )
    assert cmd[-8:] == ["-vn", "-ac", "1", "-c:a", "libmp3lame", "-b:a", "32k", "out.mp3"]
    assert "-af" not in build_mute_command(Path("in.wav"), Path("out.mp3"), [])


def test_mute_spans_silences_span(make_tone, tmp_path: Path) -> None:
    src = make_tone(4.0, 16000, 1, "wav")
    dst = mute_spans(src, tmp_path / "muted.mp3", [(1_000, 2_000)])

    info = probe(dst)
    assert (info.codec, info.channels) == ("mp3", 1)
    assert abs(info.duration_ms - 4_000) <= 150
    volumes = [_mean_volume(dst, start, 0.5) for start in (0.2, 1.25, 2.7)]
    assert volumes[1] < volumes[0] - 40
    assert volumes[1] < volumes[2] - 40


def _mean_volume(path: Path, start_s: float, seconds: float) -> float:
    res = subprocess.run(
        [
            "ffmpeg",
            "-nostdin",
            "-hide_banner",
            "-ss",
            str(start_s),
            "-t",
            str(seconds),
            "-i",
            str(path),
            "-af",
            "volumedetect",
            "-f",
            "null",
            "-",
        ],
        capture_output=True,
        check=True,
    )
    match = re.search(r"mean_volume: (-?[\d.]+|-inf) dB", res.stderr.decode("utf-8", errors="replace"))
    assert match is not None
    return float(match.group(1))
