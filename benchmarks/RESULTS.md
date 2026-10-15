# Benchmark results (measured — not estimates)

All values below were **measured** by running
`benchmarks/AusbildungsWegInterview.Benchmarks` (see [Running](#running)) on the
current host. They describe **this** machine only; treat them as a lower bound
for a comparable CPU and as indicative (not guaranteed) for stronger ones.

## Host (measurement hardware)

| Item | Value |
|---|---|
| CPU | Intel(R) Xeon(R) Platinum (shared cloud vCPU) |
| Cores (visible to process) | 2 |
| RAM | 4 GB |
| OS | Linux x64 (container) |
| GPU | none (CPU-only; no VRAM numbers exist for this host) |
| .NET | 8.0 |

## Models used

| Model | Size | Used for |
|---|---|---|
| whisper.cpp `ggml-tiny.bin` | ~75 MB | transcription, whisper `-tr` translation |
| opus-mt `de-en` (Argos) | ~100 MB (package) | offline DE→EN translation probe |

## Measured results

### Speech recognition (real whisper.cpp, 11.0 s JFK audio, 16 kHz)

| Run | Wall time | Real-time factor (RTF) |
|---|---|---|
| `whisper-cli` final transcription (tiny) | **4382 ms** (range 3585–4382 ms across runs) | **≈ 0.40×** (2.5× faster than real time) |
| `whisper-cli -tr` translation (tiny) | **2319 ms** (range 2318–2563 ms) | ≈ 0.21× |

RTF < 1 means the model keeps up with real-time audio on 2 cores, leaving
headroom for the rest of the pipeline.

### Question detection (mocked, deterministic)

| Metric | Value |
|---|---|
| Median latency, multilingual DE/EN/FR/AR | **≈ 650 ns/op** (5 reps × 20 000) |

Negligible relative to audio chunking (0.5 s) and inference.

### End-to-end pipeline (real ASR, mocked LLM stages)

Full flow: audio → PCM → chunk/silence → whisper transcription → question
detection → translation (mock) → answer (mock). 11 s clip fed as fast as
possible, then 2 s trailing silence to drive finalization.

| Milestone | Elapsed |
|---|---|
| Provisional answer (first question detected mid-segment) | **4288 ms** |
| Final transcript (end-of-speech) | **7401 ms** |
| Final answer (mock LLM = instant) | **7401 ms** |

With a real Ollama model the final answer adds the LLM generation time on top
(not measurable here — see below).

### Memory & queue behavior under sustained load

| Metric | Value |
|---|---|
| 10 min of simulated audio (1200 × 0.5 s frames) | completed, no crash |
| Max bounded-queue depth | **128 (at cap)** — `DropOldest` backpressure works |
| Peak process working set | **≈ 77 MB** (includes ~60 MB .NET runtime baseline) |

The pipeline's own growth under sustained audio is small and bounded.

### Offline translation probe (Argos Translate / opus-mt, DE→EN)

| Case | Latency | Output quality (observed) |
|---|---|---|
| First call (model load) | 5.95 s | "Why do you want to get an education in retail?" |
| Warm calls | **0.16–0.36 s** | "I finished my high school graduation in 2025 and did an FSJ." |

Argos/opus-mt is practical for DE↔EN on modest CPUs once the ~100 MB
per-pair model is downloaded (explicitly, by the user).

## What could NOT be measured on this host (stated honestly)

- **WASAPI loopback capture stability / device-change behavior** — requires
  Windows audio hardware. The capture code compiles and the decision logic is
  unit-tested, but no real Windows device was exercised here.
- **WPF UI responsiveness** at runtime — requires Windows.
- **Ollama answer/translation latency** — no Ollama instance on this host.
  (Expect O(llama) of 1–10 s per sentence on a 3B model for a 2-core CPU; this
  is an **estimate**, not a measurement.)
- **GPU/VRAM usage** — no GPU on this host.
- **Whisper models larger than `tiny`** — only `tiny` is present; `base`/`small`
  will be proportionally slower (whisper.cpp reports per-model RTF; run the
  benchmark on your machine for your model).

## Safe default settings for a modest Windows PC

Based on the measurements above (2 cores, 4 GB, CPU-only):

- **Whisper model:** `tiny` for guaranteed real-time on very modest CPUs;
  `base` (default) when you have ~4+ cores or an 8 GB machine — still near
  real-time. Avoid `small`/`medium` for live use on CPU without a strong core
  count.
- **`WhisperThreads`:** set to the physical core count (2 here).
- **`ChunkSeconds`:** 0.5 s (default) — good balance of latency vs. inference
  count.
- **Translation:** `whisper (→EN)` costs one extra tiny-model pass (~2.3 s for
  11 s of audio) — acceptable; Ollama translation adds LLM latency; External
  CLI (opus-mt) is ~0.2 s warm per sentence for supported pairs.
- **`SilenceFinalizeSeconds`:** 0.8 s (default) — finalization lag.
- Keep **bounded queue** (128) — no memory risk under sustained audio.

## Running

```powershell
# from the repo root (Windows or Linux/macOS with .NET 8)
dotnet run --project benchmarks/AusbildungsWegInterview.Benchmarks -c Release -- `
  --whisper-cli <path-to-whisper-cli> `
  --model <path-to-ggml-model.bin> `
  --wav <path-to-16k-mono-wav>
```

Without the three `--` flags the whisper items are skipped; the detection,
E2E-with-mock-ASR and sustained-memory items still run. The benchmark is a
dev tool and is **not** shipped with the application.
