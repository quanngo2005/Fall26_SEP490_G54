"""ASR benchmark: WER, real-time factor and peak VRAM per model on dev audio.

Runs the service's own transcriber (VAD, glossary prompt, hallucination filter) on 16 kHz mono copies
of `<CALL_ID>_<SETTING>.wav` files, against references from `<CALL_ID>.timeline.json` (`turns[].text`).

    cd ai-service
    python -m bench.asr_bench --data-dir <folder with wav + timeline.json> --output bench/RESULTS.md
"""

import argparse
from dataclasses import dataclass
from datetime import date
import gc
import json
from pathlib import Path
import re
import subprocess
import tempfile
import threading
import time

import jiwer

from app.pipeline.asr import FasterWhisperTranscriber
from app.pipeline.audio import normalize as normalize_audio
from app.pipeline.numbers import normalize as normalize_numbers

DEFAULT_MODELS = ("large-v3-turbo", "nunu-willump/PhoWhisper-medium-ct2")
MAX_VRAM_MB = 3584
MAX_RTF = 0.5


@dataclass(frozen=True)
class Sample:
    call_id: str
    setting: str
    wav: Path
    duration_ms: int
    reference: str


@dataclass(frozen=True)
class ModelResult:
    model: str
    wer: float
    wer_by_setting: dict[str, float]
    wer_by_call: dict[str, float]
    rtf: float
    peak_vram_mb: int | None
    load_s: float

    @property
    def eligible(self) -> bool:
        return self.rtf <= MAX_RTF and self.peak_vram_mb is not None and self.peak_vram_mb <= MAX_VRAM_MB


def wer_text(text: str) -> str:
    """Same normalisation on both sides: numbers as digits, lower case, no punctuation, digit runs joined."""
    words = re.sub(r"[^\w\s]", " ", normalize_numbers(text).text.lower()).split()
    joined: list[str] = []
    for word in words:
        if word.isdigit() and joined and joined[-1].isdigit():
            joined[-1] += word
        else:
            joined.append(word)
    return " ".join(joined)


class VramMonitor:
    """Polls `nvidia-smi` for the GPU's used memory; peak is None when nvidia-smi is unavailable."""

    def __init__(self) -> None:
        self.peak_mb: int | None = None
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._poll, daemon=True)

    def __enter__(self) -> "VramMonitor":
        self._thread.start()
        return self

    def __exit__(self, *_: object) -> None:
        self._stop.set()
        self._thread.join()

    def _poll(self) -> None:
        while not self._stop.is_set():
            used = _gpu_memory_used_mb()
            if used is None:
                return
            self.peak_mb = max(self.peak_mb or 0, used)
            self._stop.wait(0.2)


def _gpu_memory_used_mb() -> int | None:
    try:
        res = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            capture_output=True,
            check=True,
            text=True,
        )
    except (FileNotFoundError, subprocess.CalledProcessError):
        return None
    return int(res.stdout.split()[0])


def load_samples(data_dir: Path, work_dir: Path) -> list[Sample]:
    samples = []
    for wav in sorted(data_dir.glob("*_*.wav")):
        call_id, setting = wav.stem.split("_", 1)
        timeline = json.loads((data_dir / f"{call_id}.timeline.json").read_text(encoding="utf-8"))
        normalized = work_dir / f"{wav.stem}.wav"
        info = normalize_audio(wav, normalized)
        reference = " ".join(turn["text"] for turn in timeline["turns"])
        samples.append(Sample(call_id, setting, normalized, info.duration_ms, reference))
    return samples


def run_model(model: str, samples: list[Sample], compute_type: str) -> ModelResult:
    transcriber = FasterWhisperTranscriber(model, device="cuda", compute_type=compute_type)
    baseline_mb = _gpu_memory_used_mb()
    with VramMonitor() as vram:
        started = time.perf_counter()
        transcriber.transcribe(samples[0].wav, samples[0].duration_ms)  # warm-up: loads the model
        load_s = time.perf_counter() - started

        hypotheses: dict[str, str] = {}
        elapsed = 0.0
        for sample in samples:
            started = time.perf_counter()
            segments = transcriber.transcribe(sample.wav, sample.duration_ms)
            elapsed += time.perf_counter() - started
            hypotheses[sample.call_id] = " ".join(s.text for s in segments)
    del transcriber
    gc.collect()

    def _wer(selected: list[Sample]) -> float:
        return jiwer.wer([wer_text(s.reference) for s in selected], [wer_text(hypotheses[s.call_id]) for s in selected])

    settings = sorted({s.setting for s in samples})
    peak = None if vram.peak_mb is None or baseline_mb is None else vram.peak_mb - baseline_mb
    return ModelResult(
        model=f"{model} ({compute_type})",
        wer=_wer(samples),
        wer_by_setting={name: _wer([s for s in samples if s.setting == name]) for name in settings},
        wer_by_call={s.call_id: _wer([s]) for s in samples},
        rtf=elapsed / (sum(s.duration_ms for s in samples) / 1000),
        peak_vram_mb=peak,
        load_s=load_s,
    )


def render(results: list[ModelResult], samples: list[Sample]) -> str:
    settings = sorted({s.setting for s in samples})
    eligible = [r for r in results if r.eligible]
    chosen = min(eligible, key=lambda r: r.wer) if eligible else None
    total_min = sum(s.duration_ms for s in samples) / 60_000

    lines = [
        "# ASR benchmark (AI-03)",
        "",
        f"Run {date.today():%d/%m/%Y} by `python -m bench.asr_bench`. **Synthetic gTTS dev data, not a test result:** "
        f"{len(samples)} dev files ({total_min:.1f} min, DESK = room noise, CALL = 8 kHz telephone band) generated "
        "with gTTS from the recording-kit-v2 scripts; references are the script lines. Real recordings will score worse.",
        "",
        "WER after the same normalisation on both sides (spoken numbers to digits, lower case, no punctuation). "
        "RTF = processing time / audio duration, model load excluded (one warm-up file). "
        "VRAM = peak `nvidia-smi` memory.used minus the idle baseline.",
        "",
        "| Model | WER | " + " | ".join(f"WER {name}" for name in settings) + " | RTF | Peak VRAM (MB) | Load (s) |",
        "|---|---|" + "---|" * len(settings) + "---|---|---|",
    ]
    for r in results:
        vram = "n/a" if r.peak_vram_mb is None else str(r.peak_vram_mb)
        by_setting = " | ".join(f"{r.wer_by_setting[name]:.1%}" for name in settings)
        lines.append(f"| {r.model} | {r.wer:.1%} | {by_setting} | {r.rtf:.3f} | {vram} | {r.load_s:.1f} |")

    lines += ["", "Per file WER:", "", "| File | " + " | ".join(r.model for r in results) + " |"]
    lines.append("|---|" + "---|" * len(results))
    for s in samples:
        lines.append(f"| {s.call_id}_{s.setting} | " + " | ".join(f"{r.wer_by_call[s.call_id]:.1%}" for r in results) + " |")

    rule = f"lowest WER among models with peak VRAM ≤ {MAX_VRAM_MB} MB and RTF ≤ {MAX_RTF}"
    decision = f"**{chosen.model}**" if chosen else "none meets the limits"
    lines += ["", f"Decision ({rule}): {decision}.", ""]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("RESULTS.md"))
    parser.add_argument("--models", nargs="+", default=list(DEFAULT_MODELS))
    parser.add_argument("--compute-type", default="int8_float16")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="asr-bench-") as work_dir:
        samples = load_samples(args.data_dir, Path(work_dir))
        results = [run_model(model, samples, args.compute_type) for model in args.models]
    args.output.write_text(render(results, samples), encoding="utf-8")
    for r in results:
        print(f"{r.model}: WER={r.wer:.3f} RTF={r.rtf:.3f} VRAM={r.peak_vram_mb}MB")


if __name__ == "__main__":
    main()
