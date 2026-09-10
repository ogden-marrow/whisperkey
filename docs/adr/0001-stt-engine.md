# ADR 0001 — Local speech-to-text engine

Status: accepted · Issue #1

## Decision

**Parakeet TDT 0.6b v3 (int8) via sherpa-onnx, CPU only.** Whisper stays out of the
dependency list entirely.

## Why this needed measuring

The published comparisons say "large-v3-turbo is ~8x faster than large-v3", which is
true — on a GPU. Every number below was measured on the actual target machine, CPU
only, because that is what this app will do.

## Method

- Machine: i7-11800H (8c/16t), 32 GB RAM, RTX 3050 Ti 4 GB, Windows 11 26200.
- Clip: 12.26 s of synthesized speech, three sentences, known transcript.
- Each engine ran in its own process (a shared process makes the memory numbers
  meaningless). Median of 5 runs for sherpa-onnx, 3 for Whisper.net.
- `private` is `Process.PrivateMemorySize64` with the model warm.
- Harness is throwaway and deliberately not in this repo.

## Results

| Engine | Load | Private RAM | Median transcribe | RTFx | Transcript |
|---|---|---|---|---|---|
| Parakeet TDT 0.6b v3 int8 | 1879 ms | 972 MB | **747 ms** | 16.4x | 1 word wrong (`a`/`the`) |
| Parakeet TDT-CTC 110m en | 1245 ms | 749 MB | **195 ms** | 62.9x | perfect, no final period |
| Whisper base.en | 119 ms | 165 MB | 1177 ms | 10.4x | perfect |
| Whisper large-v3-turbo q5_0 | 279 ms | 570 MB | **16720 ms** | **0.7x** | perfect |

## What the numbers actually say

**Whisper large-v3-turbo is disqualified.** 16.7 seconds of waiting after you stop
talking, for 12 seconds of speech. On CPU it runs *slower than real time*. It is a
GPU model, and putting it on the GPU would drag a CUDA runtime into a tray app that
is supposed to be small and start instantly. Not a trade worth making.

**Accuracy did not separate the survivors here, and that is a limitation of this
test.** The clip is clean synthesized speech, so everything got it essentially right.
The published Open ASR numbers are the better guide for messy real speech: Parakeet
v3 ~6.3% WER against Whisper base.en's low-to-mid teens. That gap is the whole reason
base.en loses despite being 6x lighter.

**Memory is fixed cost, not a tunable.** I swept 1/2/4/8 threads expecting ONNX
Runtime's per-thread arenas to dominate. They do not — private bytes stayed within
2 MB across the whole sweep while latency moved 3x. So there is no configuration
that gives Parakeet accuracy at Whisper-base memory.

## The cost, stated plainly

Parakeet 0.6b keeps **~970 MB resident** for as long as the app runs, because the
model is held warm to meet the sub-150 ms start-listening budget. On this 32 GB
machine that is about 3% of RAM and a fair trade. On an 8 GB machine it would not be.
This is the one place the app is not lightweight, and it is a deliberate choice
rather than an oversight.

Users who disagree get the lever in config rather than an argument:

| `model` | Private RAM | 12 s clip | Use when |
|---|---|---|---|
| `parakeet-0.6b-v3` (default) | ~970 MB | ~750 ms | You want the best accuracy |
| `parakeet-110m-en` | ~750 MB | ~195 ms | English only, want it faster |
| `whisper-base-en` | ~165 MB | ~1180 ms | RAM matters more than accuracy |

## Consequences

- Dependencies: `org.k2fsa.sherpa.onnx` + `org.k2fsa.sherpa.onnx.runtime.win-x64`
  (1.13.5). No CUDA, no Python, no cloud.
- 4 threads is the default: 695 ms versus 747 ms at 8, and it leaves cores free so
  dictating never makes the rest of the machine feel sluggish.
- Model auto-downloads on first run (~465 MB) to `%LOCALAPPDATA%`.
- Parakeet v3 covers 25 European languages. Non-European languages are not supported;
  if that is ever needed, it is a Whisper fallback and a separate decision.
- Revisit if a quantized large-v3-turbo lands with usable CPU throughput, or if
  sherpa-onnx exposes a DirectML provider (which would use the 3050 Ti without CUDA).
