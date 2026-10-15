# AusbildungsWeg Interview

**A local-first AI interview assistant for Windows.**

AusbildungsWeg Interview listens to the **interviewer's system audio** (Windows
Audio Loopback), transcribes the questions in real time with **whisper.cpp**,
detects and translates the questions, and generates **context-aware answer
suggestions** grounded in your CV and the job description with a local
**Ollama** model — all on your own computer.

> **Purpose & ethics.** This tool supports interview *preparation* and permitted
> interview assistance. You are responsible for following the rules of your
> interview and for **disclosing AI assistance when required**. The app does not
> implement covert recording, hidden capture, or any feature designed to bypass
> interview restrictions.

---

## Status — Phase 2 (local AI interview engine & real-time speech)

**Verified in this repository (automated, cross-platform test host = Linux):**
- The solution and all 8 projects build (Release, 0 warnings / 0 errors).
  The Windows-only projects (App, Audio) compile via `EnableWindowsTargeting`.
- **141 unit + integration tests pass** (`dotnet test -c Release`), covering:
  - Real **whisper.cpp integration**: a genuine `whisper-cli` subprocess run
    against `ggml-tiny.bin` correctly transcribes the `jfk.wav` speech sample
    (auto-detected language: English), produces marked *partial* results, and
    fails clearly on a corrupt model. These tests self-skip when no local
    whisper.cpp build is present (see [Running the whisper tests](#running-the-whisper-integration-tests)).
  - Real-time pipeline: PCM chunking, silence gating that never discards quiet
    speech, silence-driven **finalization**, bounded-queue **backpressure**,
    translation **dedup**, provisional→final answers, **cancellation of the
    in-flight answer on Stop**, repeated start/stop cycles, and bounded
    **capture auto-restart** after a device failure.
  - Multilingual **question detection** (DE/EN/FR/AR), statement vs. question
    vs. incomplete fragment handling.
  - Translation providers: pass-through labeling, "Ollama unavailable", model
    echo ("unchanged, not a translation"), failure and empty-output cases.
  - Grounded answer generation: CV/JD grounding, **no-fabrication guardrails**,
    style instructions, recent-conversation context, Ollama unavailable /
    missing model / timeout / malformed response, cancellation propagation.
  - Ollama HTTP client: availability, model-name matching, timeouts, error
    field, malformed JSON.
  - Structured **redacting logger** (GitHub PAT / bearer tokens / long opaque
    strings are redacted defensively).
  - Config defaults & model-path resolution; WAV writer header correctness.

**Requires a Windows machine (Windows 10/11) — not verifiable on the Linux build host:**
- The **WPF application** launching and the floating UI (`App`, `net8.0-windows`).
- **WASAPI loopback capture** of a real output device (`Audio`) — the capture
  code compiles cross-platform, but only Windows can exercise it.
- Live end-to-end behavior: real device → real transcription → real Ollama
  answers in the UI.

On Windows, verify with:
```powershell
dotnet build AusbildungsWegInterview.sln -c Release
dotnet test -c Release
dotnet run --project src/AusbildungsWegInterview.App -c Release
```

---

## Requirements

### To build & run the app (Windows)
- **Windows 10 or 11** (x64)
- **.NET 8.0 SDK** (or the .NET 8 Desktop Runtime for a published build)
- **whisper.cpp** (either mode, your choice — see below):
  1. *CLI mode (recommended, easiest):* a `whisper-cli` executable + a model
     file. Build instructions: [Setting up whisper.cpp](#setting-up-whispercpp).
  2. *Native-library mode:* a `libwhisper` build next to the app (advanced).
- (Optional) **Ollama** for answer suggestions + LLM translation:
  <https://ollama.com/download/windows>

### To run the automated tests (any OS with the .NET 8 SDK)
- `dotnet test` runs the full cross-platform suite on Windows, macOS, or Linux.
- The whisper.cpp integration tests additionally need a local whisper.cpp build
  (detected automatically; see below).

---

## Repository layout

```
AusbildungsWegInterview.sln
src/
  AusbildungsWegInterview.App/            WPF UI, view models, composition root (Windows)
  AusbildungsWegInterview.Audio/          WASAPI loopback capture (Windows)
  AusbildungsWegInterview.Core/           Domain models, provider interfaces, pipeline
  AusbildungsWegInterview.Speech/         whisper.cpp adapters (CLI subprocess + native lib)
  AusbildungsWegInterview.Translation/    Translation providers (Ollama, pass-through)
  AusbildungsWegInterview.Answers/        Question detection, context, answer generation
  AusbildungsWegInterview.Infrastructure/ Ollama client, config store, model manager, logging
tests/
  AusbildungsWegInterview.Tests/          xUnit unit + integration tests (cross-platform)
docs/
  ARCHITECTURE.md
```

---

## Build & run (Windows)

```powershell
# 1) Build the full solution
dotnet build AusbildungsWegInterview.sln -c Release

# 2) Run the app
dotnet run --project src/AusbildungsWegInterview.App -c Release
```

Or publish a Windows executable:
```powershell
dotnet publish src/AusbildungsWegInterview.App -c Release -r win-x64 --self-contained false
```

### First-run setup (in the app)

1. **Select the output device** to listen from (the device the interviewer's
   voice is heard on — usually the default). Press **Refresh** if it is missing.
2. **Configure speech recognition** (Settings):
   - Set **Whisper CLI path** to your `whisper-cli` executable, **or** leave it
     empty to use the native-library adapter.
   - Pick the **speech model** (`tiny`/`base`/`small`/`medium`) and press
     **Download** (explicit download — nothing is fetched automatically).
     You can also set **Model path** to a model file you already have.
3. (Optional) **Ollama**: start Ollama, ensure the model is pulled
   (`ollama pull llama3.1:8b`). The window shows whether Ollama is running and
   whether the configured model is present, with the exact `ollama pull`
   command when it is missing. The UI also shows conservative model
   suggestions for the detected RAM.
4. (Optional) **Load your CV** and **job description** (text files). They stay
   on disk in memory only and are used as *verified facts* for grounded
   answers.
5. Press **Start Listening**. The window shows a clear
   **"System audio is being captured"** indicator — the microphone is never used.

### Verifying that only system audio is captured

- The app only ever creates a **loopback** capture session on a **playback**
  device; it contains no microphone capture code path and no microphone
  permission request.
- Practical check: mute or disconnect your microphone, play audio from the
  selected output device, and confirm transcription works.
- The visible "System audio is being captured (loopback…)" indicator is only
  shown while the loopback session is actually running; a failed or stopped
  capture is reported as an error, never as "listening".

---

## Setting up whisper.cpp

Two supported modes, selected automatically:

| Mode | When used | What you provide |
|---|---|---|
| **CLI subprocess** (recommended) | `Whisper CLI path` is set and the file exists | `whisper-cli` executable + model `.bin` |
| **Native library** | `Whisper CLI path` is empty | a `libwhisper` build discoverable next to the app |

### Building whisper.cpp (CLI mode)

```bash
git clone https://github.com/ggerganov/whisper.cpp
cd whisper.cpp
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release -j
# Linux/macOS: ./build/bin/whisper-cli
# Windows:     build\bin\whisper-cli.exe
```

Download a model explicitly (nothing is fetched automatically by this app
unless you press Download in the UI or run the script):
```bash
cd whisper.cpp
./models/download-ggml-model.sh tiny    # or: base, small, medium
```

Then in the app: **Whisper CLI path** = `…/build/bin/whisper-cli(.exe)`,
**Model path** = `…/models/ggml-tiny.bin` (or pick the model name and press
Download, which saves to your local user data folder).

### Model size / quality trade-offs

| Model | Size (quantized) | Notes |
|---|---|---|
| `tiny` | ~75 MB | Fastest, lowest accuracy; good for testing |
| `base` | ~142 MB | Default; reasonable DE/EN accuracy on modest CPUs |
| `small` | ~466 MB | Better accuracy, clearly slower on CPU |
| `medium` | ~1.5 GB | Best accuracy; needs RAM + patience |

Multilingual models (without `.en`) are used so that German, English, French
and Arabic are supported; the language can be pinned in Settings or left on
**auto-detect**. This integration transcribes **discrete accumulated segments**
(re-running whisper on the current segment buffer on a cadence) and honestly
labels results as *partial* (provisional) vs. *final* — it does not claim
token-level streaming.

### Running the whisper integration tests

The tests auto-detect a local build (repo sibling `tools/whisper.cpp` or env
var `AUSBILDUNGSWEG_WHISPER_CLI`). With a build + `ggml-tiny.bin` +
`samples/jfk.wav` present, they run a **real** transcription; without one they
report "SKIPPED" in the test output and the remaining error-path tests (missing
executable/model, hanging executable timeout, corrupt model, WAV header) still
run.

---

## Translation

- **Provider:** the local Ollama model (optional). With Ollama unavailable the
  app still transcribes; the translation panel shows
  **"Ollama unavailable – original text shown (not translated)"** instead of
  falsely labeling the original as translated.
- **Pass-through mode:** uncheck **Enable translation** in Settings — the
  original is shown and clearly labeled *"Translation disabled (pass-through)"*.
- **Honest labeling:** if the model returns the text unchanged (e.g. source and
  target language are the same), the panel says so — unchanged text is never
  labeled "translated".
- The **complete question** is translated (segment finalization), not isolated
  words; unchanged segment text is not re-translated (dedup).
- **Offline alternatives** were evaluated for the future: a dedicated local
  translation model (e.g. `opus-mt` via a local runtime, ~200–500 MB, strong
  for common language pairs but heavy to integrate on CPU and limited for AR),
  and LLM-based translation (current approach, already present when Ollama is
  installed — no extra dependency, good quality with a ≥3B model). The
  `ITranslator` interface allows plugging a dedicated engine in later.

---

## Answer generation

- Grounded in the **question + your CV + the job description + recent
  conversation** (bounded: last 3 turns, truncated prompts).
- **Honesty rules are enforced in the prompt**: the model may only assert facts
  present in your supplied material; it must never invent qualifications,
  history, proficiency, achievements or experiences. Unsupported specifics
  become a neutral general answer.
- **Style:** Concise / Natural / Professional; **length:** Concise / Normal /
  Detailed; **language:** DE/EN/FR/AR — all in Settings.
- Answers appear *provisional* once (when a question is detectable mid-speech)
  and are *refined to final* when the question completes. Pressing **Stop**
  cancels any in-flight generation.
- If Ollama is down / model missing / request times out / output is malformed,
  the panel shows the specific, actionable problem (e.g. the exact
  `ollama pull <model>` command) instead of a fake answer.

---

## Models (downloads: explicit, local)

- **Speech (whisper.cpp):** `tiny`, `base`, `small`, `medium` are available via
  the in-app **Download** button or the official whisper.cpp scripts. Files are
  saved to your local user data folder (gitignored) and are **never committed**
  to Git. No model is downloaded at startup.
- **Answer/translation (Ollama):** uses a model already pulled on your local
  Ollama server. **Nothing is pulled automatically by this app.**

**Licensing:** whisper.cpp is MIT-licensed. Speech models are distributed in
the official whisper.cpp releases — review the applicable model license before
use beyond personal interview preparation. Model binaries are treated strictly
as **data**, never executed as code. Ollama's local models are free, open
weights (check each model's license, e.g. Llama 3.1 community license).

---

## Privacy & security

- **System-output loopback only.** The microphone is never captured.
- **No hidden recording / silent background capture.** Capture runs only while
  **Start Listening** is active, with a visible indicator, and **Stop**
  immediately tears down the capture session and cancels queued processing.
- **No persistent audio** and **no persistent transcripts** by default
  (`PersistAudio` / `PersistTranscripts` exist as explicit opt-ins, off).
- No server, no cloud, no subscription, **no API key** for the default workflow.
  **No Azure** is used or required; no cloud resources are created.
- Imported CV / job-description text is kept **local only** (in memory while
  the app runs) and is never logged.
- **Logging:** a local structured log (`%AppData%\AusbildungsWegInterview\logs\app.log`)
  records operational state only (state transitions, error types, backpressure)
  and **defensively redacts** anything that looks like a GitHub PAT, bearer
  token, or long opaque string. It never contains audio, transcript text, or
  CV content.
- `.gitignore` excludes model binaries, audio, transcripts, and any secrets.
- System-output (loopback) capture may include sound from **other applications**
  on the machine — expected for loopback; the UI documents it.

---

## Testing

```powershell
dotnet test -c Release
```

141 tests run anywhere with the .NET 8 SDK (verified on Linux). The
whisper.cpp *real transcription* tests additionally require a local whisper.cpp
build + `ggml-tiny.bin` + `jfk.wav` and self-report "SKIPPED" in the test
output when those are absent; all error-path tests run regardless.

**Not executable on the Linux build host (Windows-only):** WASAPI loopback
capture of a real device, the WPF window/ViewModel at runtime, and live
end-to-end interviews. These are compiled (via `EnableWindowsTargeting`) and
unit-mocked, but must be verified on Windows as listed under
[Status](#status--phase-2-local-ai-interview-engine--real-time-speech).

---

## Configuration

Settings are saved locally under `%AppData%\AusbildungsWegInterview\config.json`.
They contain **no secrets**: source language, translation on/off + target
language, answer language/length/style, speech model + CLI path + model path,
whisper threads, Ollama model + request timeout, output device, always-on-top,
chunk/silence tuning, and the privacy opt-ins (audio/transcript persistence).

Changing the whisper CLI path, model, or translation toggle while not listening
rebuilds the speech pipeline automatically (your CV/JD context is preserved).

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| "No active audio output device found" | Connect speakers/headphones, then **Refresh**. |
| "whisper.cpp executable not found at …" | Set the **Whisper CLI path** to the `whisper-cli(.exe)` you built. |
| "whisper.cpp model not found at …" | Press **Download** for the model, or set **Model path** to an existing `ggml-*.bin`. |
| "The native whisper runtime (libwhisper) is not available" | You're in native-library mode without a libwhisper build — set the CLI path instead (easier). |
| "whisper.cpp exited with code …" | Binary/model mismatch or corrupt model — re-download the model; check `whisper-cli -h` works on its own. |
| "whisper.cpp transcription timed out" | Model too large for the CPU — use `tiny`/`base`, or raise `OllamaRequestTimeoutSeconds`-style patience by choosing a smaller model. |
| "Local AI (Ollama): not running" | Start Ollama; answers/LLM-translation are optional. Transcription unaffected. |
| "model '…' MISSING – run: ollama pull …" | Run the shown `ollama pull` command, or pick a smaller model in Ollama. |
| "Text returned unchanged by the model" | Source and target language are the same, or the model echoed input — expected, not an error. |
| Transcription is quiet/empty | The device may be muted or silent — loopback captures whatever is *playing*. |
| Restarting whisper settings has no effect | Stop listening first — the window blocks changes while capture runs. |

---

## Known limitations (Phase 2)

- Transcription is **segment-based**, not token-streaming: partials refresh on
  a configurable cadence (default 1.0 s of new audio) and final results arrive
  after sustained trailing silence (default 0.8 s). Latency is bounded by the
  model size and CPU; measure on your hardware (the tests time the real runs).
- Very long questions are bounded by `maxSegmentSeconds` (30 s) inside the
  recognizer; interview questions are far shorter.
- Ollama translation quality follows the local model size; a ≥3B model is the
  practical minimum for good DE/EN quality.
- The question detector is a deterministic multilingual heuristic (words,
  phrases, `?`/`؟`), not a model; it favors not answering short fragments.
