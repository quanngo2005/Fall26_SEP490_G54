# ASR benchmark (AI-03)

Run 23/11/2026 by `python -m bench.asr_bench`. **Synthetic gTTS dev data, not a test result:** 15 dev files (15.6 min, DESK = room noise, CALL = 8 kHz telephone band) generated with gTTS from the recording-kit-v2 scripts; references are the script lines. Real recordings will score worse.

WER after the same normalisation on both sides (spoken numbers to digits, lower case, no punctuation). RTF = processing time / audio duration, model load excluded (one warm-up file). VRAM = peak `nvidia-smi` memory.used minus the idle baseline.

| Model | WER | WER CALL | WER DESK | RTF | Peak VRAM (MB) | Load (s) |
|---|---|---|---|---|---|---|
| large-v3-turbo (int8_float16) | 4.7% | 4.6% | 4.8% | 0.093 | 1296 | 27.5 |
| nunu-willump/PhoWhisper-medium-ct2 (int8_float16) | 13.3% | 12.5% | 14.0% | 0.687 | 2956 | 35.6 |

Per file WER:

| File | large-v3-turbo (int8_float16) | nunu-willump/PhoWhisper-medium-ct2 (int8_float16) |
|---|---|---|
| C01_DESK | 3.2% | 7.7% |
| C02_CALL | 3.8% | 2.6% |
| C03_DESK | 3.8% | 17.0% |
| C04_CALL | 2.7% | 9.3% |
| C09_DESK | 7.3% | 15.9% |
| C12_CALL | 6.0% | 18.1% |
| C13_CALL | 2.6% | 15.4% |
| C14_DESK | 6.2% | 20.0% |
| C18_CALL | 7.3% | 16.5% |
| C19_DESK | 5.6% | 14.0% |
| C20_CALL | 8.0% | 23.0% |
| C22_DESK | 4.9% | 20.7% |
| C25_DESK | 5.9% | 20.6% |
| C30_CALL | 4.8% | 20.2% |
| C31_DESK | 3.7% | 4.9% |

Decision (lowest WER among models with peak VRAM ≤ 3584 MB and RTF ≤ 0.5): **large-v3-turbo (int8_float16)**.
