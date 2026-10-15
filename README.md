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

## Status — Phase 3 (Windows integration, performance, local translation & release packaging)

**Measured on the build host** (Intel Xeon Platinum, 2 vCPU, 4 GB RAM, Linux x64, CPU-only —
full data: [benchmarks/RESULTS.md](benchmarks/RESULTS.md)):
- whisper.cpp `tiny` transcribes an 11 s speech clip in **~3.6–4.4 s (RTF ≈ 0.40)** — faster
  than real time on 2 cores, with headroom for the rest of the pipeline.
- whisper `-tr` translation (any language → English, reuses the speech model): **~2.3 s** for 11 s.
- Question detection: **~650 ns/op** (negligible).
- Full pipeline E2E (real ASR, mocked LLM): provisional answer **~4.3 s**, final transcript
  **~7.4 s** after the audio ends being fed (2 s trailing silence included).
- 10 minutes of simulated sustained audio: queue stays **≤ 128 (bounded)**, peak process RSS
  **≈ 77 MB** (incl. runtime baseline).
- Argos/opus-mt DE→EN probe (evaluation): **0.16–0.36 s warm** per sentence, ~100 MB per pair.

**Verified by the automated suite (320 tests, cross-platform):**
- **Real end-to-end run**: actual JFK speech audio through the complete pipeline
  (real whisper.cpp → question detection → translation → answer), asserting the
  real transcript, a real generated answer, and honest stop semantics.
- **Stop reliability (Phase 1 hardening)**: Stop cancels the in-flight whisper.cpp
  subprocess promptly (test: 69 ms), queued audio is **not** transcribed after Stop,
  concurrent Stops (UI stop + app shutdown) share one teardown, and a capture failure
  at initial start reports exactly one error without an auto-restart race.
- **Localized runtime errors**: every pipeline / capture / whisper / Ollama failure is
  category-mapped to a short message in all four UI languages (en/de/fr/ar); raw
  exception text (which can contain local file paths) is never shown.
- **Question detection & translation (Phase 2)**: a finalized segment is only treated
  as a question when it carries an interrogative cue (question mark, question phrase,
  or interrogative word — DE/EN/FR/AR, incl. imperative patterns like "Sagen Sie
  mir…", "Tell me about…", "Parlez de…", "حدثني عن…"). Interviewer statements and
  fillers are still transcribed and translated, but never answered. Repeated finalized
  questions are not re-answered (normalized repeat memory, case/punctuation/Arabic
  orthography insensitive), and a final text identical to the already-answered
  provisional text is refined in place without a new LLM call. Translation dedup keeps
  identical segment text from being re-requested; the final-segment translation path is
  cancellation-aware (Stop cancels it).
- **Honest, localized translation status**: every translation outcome carries a
  machine-readable status code (pass-through, echo, provider unavailable/failed, no
  output, pinned-source required, pair not supported, CLI launch/timeout/error,
  whisper English-only/engine-unsupported) rendered as a short line in all four UI
     languages; the raw English provider message is log-only and never displayed.
- **CV & job-description import (Phase 3)**: PDF / DOCX / TXT import — the file
  content is *sniffed* (PDF header, ZIP/DOCX structure, plain text), extensions
  alone are never trusted; size is validated first (10 MB default), extraction is
  capped (60 000 chars, truncation flagged), and every failure (unsupported /
  empty / corrupt / oversized / no extractable text) is a categorized, localized
  error in all four UI languages. Extracted text lands in an editable **review
  box** — nothing grounds answers until you press **Use**; a replacement import
  keeps the old grounding active until the new text is accepted; **Clear**
  removes the material from UI *and* answer context. Import is memory-only:
  no disk writes, no content logging, no telemetry (verified by a directory
  snapshot/diff test). The answer prompt explicitly marks imported material as
  DATA, not instructions (prompt-injection guard), and missing evidence must be
  acknowledged or given as a *clearly marked template* — never fabricated.
- **Answer generation reliability (Phase 4)**: every failed/unavailable
  generation carries a machine `AnswerStatus` (runtime unavailable / model
  missing / failed / timed out / empty) rendered through a localized status
  line in all four UI languages — the raw English provider message is log-only
  and never displayed. A request *timeout* is distinguished from *Stop /
  superseded* (a hung model yields an honest "timed out" status; a stop stays
  silent), and a generation that completes in the same instant as Stop is
  discarded instead of updating the UI after the session ended. Ollama
  availability + model presence are re-probed while the session runs (default
  20 s window), so starting Ollama or pulling the model mid-session is picked
  up without a restart. Status messages are no longer recorded as conversation
  turns (no English boilerplate in the grounding context). The answer panel
   shows a review disclaimer and a localized Copy action (real suggestions
   only).
- **UX integration & reliability (Phase 5)**: deterministic Start/Stop — a
  second Start during the async Preparing window is a no-op, and a failed
  start (device missing/locked) leaves a retryable Faulted state after
  releasing its token sources + frame channel (no app restart needed). An
  honest "Generating answer…" state is shown while the model thinks for a
  question that has no provisional yet (never for in-place refinements).
  Dynamic UI strings (error lines, translation status, answer status) are
  re-localized on a UI-language switch — no stale translations survive. Error
  messages no longer embed raw exception text (which can contain local paths):
  the two pre-existing `…: {0}` device/download templates are now
  self-contained. Privacy invariants are test-enforced: the persisted config
  can never carry CV/JD/transcript/answer content (reflection + JSON scan),
  and a WPF-free theme audit verifies Light/Dark define identical resource
  keys and every referenced resource is defined.
- Device-change decision logic (explicit selection kept; default-follow re-opens on the
  new default; lost device → fatal error → bounded pipeline restart).
- Answer quality regressions: question-specific prompts, language/length respect, bounded
  material, genuine-translation hand-off (pass-through is never treated as a translation),
  bounded recent-conversation context (a question is never in its own context).
- Translation providers: whisper `-tr` (real subprocess, English-only honesty), external CLI
  (real subprocess stdin/stdout, unsupported-pair / timeout / echo honesty).
- Plus all Phase 1/2/3/4/5 coverage (pipeline, cancellation, backpressure,
   Ollama failure modes + answer statuses/timeouts/recovery, start-window
   races + failed-start recovery, document import + review states, catalog
   raw-English leak guard, theme resource consistency, config privacy
   boundary, redacting logger, config, WAV writer).

**Requires a Windows machine (Windows 10/11 x64) — NOT verifiable on the Linux build host
and therefore NOT claimed as verified:**
- WASAPI loopback capture on a real output device, live device plug/unplug, default-device
  switching on real hardware, and the WPF window/ViewModel at runtime. The published
  `win-x64` build was produced cross-platform and its file layout verified, but it has
  **not been launched on Windows** in this milestone.
- Pressing **Stop while transcription is active** on real hardware (the cancellation
  logic is unit-tested with fakes and real subprocesses; the WASAPI device release
  itself needs Windows). Expected: the UI returns to "Stopped" within ~1 s, the capture
  indicator turns off, and no further transcript updates appear.

On Windows, verify with:
```powershell
dotnet build AusbildungsWegInterview.sln -c Release
dotnet test -c Release
.\scripts\publish.ps1 -Zip     # then run publish\AusbildungsWegInterview-windows-x64\AusbildungsWegInterview.exe
```

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
  AusbildungsWegInterview.App/            WPF UI, view models, Windows bootstrap (Windows)
  AusbildungsWegInterview.Composition/    Composition root (AppServices), testable on any OS
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

### Interface (Phase 3 UI redesign)

The window is restyled to match the **ausbildungsweg.net** brand (light `#F6F6FB`
surface, navy ink, violet/blue accent `#6D5DF6`, brand gradient buttons,
Segoe UI, rounded cards) — see `src/AusbildungsWegInterview.App/Themes/AwTheme.xaml`
for the token map extracted from the site's CSS.

- **Primary screen** = the four interview choices only: *interview language*,
  *translation language*, *suggested-answer language*, and *answer level (CEFR
  A1–C2)*, plus one prominent **Start listening** button, a **Stop** button, and
  a compact live status chip in the header.
- **Live panels**: original transcript, detected question, translation (with
  honest status), and the suggested answer (visually prominent).
- **Advanced settings** (collapsed expander): output device + refresh, speech
  model + download, whisper CLI/model paths, translation engine + external CLI,
  Ollama status + model, answer length/style, CV/job-description loading.
- The CEFR level is passed to the answer generator per request (persisted in the
  existing config); it changes vocabulary/grammar complexity, not the pipeline.
- **UI language & theme**: the header offers an interface-language selector
  (English, Deutsch, Français, العربية) and a light/dark toggle. Every UI string
  comes from static resource catalogs (`AusbildungsWegInterview.Localization`,
  one resx per language) - no runtime machine translation. The Arabic UI uses
  right-to-left layout (WPF FlowDirection). Both preferences persist in the
  existing config file, and the UI language stays independent of the interview /
  translation / answer languages.
- **Branding & dark theme**: the header shows the AusbildungsWeg logo
  (`Assets/AwLogo.png`, committed to the repo, packaged as a WPF resource) and
  the executable carries the multi-size `Assets/app.ico` (16-256 px). The dark
  theme is a refined navy/slate variant of the brand palette with layered,
  distinct surfaces (window < cards < inner panels), fully themed combo popups,
  check boxes and the Advanced-settings expander — no white/unsurfaced areas
  (see `Themes/AwTheme.Light.xaml` / `AwTheme.Dark.xaml`).

### First-run setup (in the app)

1. **Select the output device** to listen from (the device the interviewer's
   voice is heard on — usually the default) in *Advanced settings*; press
   **Refresh** if it is missing.
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

## Translation (three local engines + pass-through)

Pick the **Translation engine** in Settings. All options are local and offline;
none needs a cloud service or API key.

| Engine | Coverage | Extra dependency | Measured latency* | Notes |
|---|---|---|---|---|
| **Ollama (local LLM)** — default | All pairs (DE↔EN, DE↔FR, DE↔AR, …) with a ≥3B model | Ollama + pulled model | not measured here (no Ollama on build host; expect seconds/sentence on CPU) | Best quality & coverage; the LLM you already use for answers |
| **whisper.cpp (→ English only)** | Any source → **EN** | **none** (reuses the speech model) | ~2.3 s per 11 s segment (tiny) | Zero-setup offline path; other targets report "use Ollama", never fake |
| **External CLI (opus-mt class)** | Pairs you download (e.g. DE↔EN, DE↔FR, EN↔AR) | Standalone MT binary + per-pair model (user-managed) | ~0.2 s warm/sentence (Argos probe, DE→EN) | See setup below |
| **Off** (uncheck Enable translation) | — | — | — | Original shown, clearly labeled "pass-through" |

\* Measured on the build host (2 vCPU / 4 GB, CPU-only) — see [benchmarks/RESULTS.md](benchmarks/RESULTS.md).

**Honest labeling is enforced by design:** un-translated text is never labeled
"translated". Each engine reports its real status — Ollama unavailable, CLI
missing, pair not covered, source not pinned, model echoed the input, timeout —
and the UI shows that status. The complete question is translated (segment
finalization), not isolated words, and unchanged text is not re-translated
(dedup).

### External CLI setup (e.g. opus-mt via mariannet)

1. Obtain a standalone MT CLI. Example: build [mariannmt](https://github.com/marian-nmt/mariannmt)
   (Apache-2.0) or use an Argos wrapper; the app only needs a binary that reads
   text on **stdin** and writes the translation on **stdout (first line)**.
2. Download a model explicitly (the app never downloads models). Example
   (opus-mt DE→EN, ~260 MB, from the official Marian/Opus releases — check the
   license before redistribution):
   ```powershell
   # mariannet example
   & .\mariannet.exe -m C:\models\opus-mt-de-en.bin -s de -t en -i question.txt -o answer.txt
   ```
3. In Settings → **External CLI**: binary path, args template with `{source}` /
   `{target}` placeholders (e.g. `mariannet -m C:\models\opus-mt-de-en.bin -s {source} -t {target} -i - -o -`),
   and the supported pairs (e.g. `de-en,en-de`).

**Evaluation evidence (Phase 3):** Argos Translate (opus-mt) was installed and
measured on the build host — DE→EN quality good, 0.16–0.36 s warm, ~100 MB per
pair, but its package index covers only `de↔en` and `en↔ar` (no `de↔fr`), and
it is a Python package (extra runtime on Windows). NLLB-200/M2M100 cover all
pairs but need ~2–3 GB models (impractical on modest machines). whisper `-tr`
covers →EN with zero extra dependency. The opus-mt/mariannet CLI is the most
practical small-model route for dedicated pairs, which is what the External CLI
provider standardizes. Details in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

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

## Windows Release (packaging)

Reproducible publishing (run on Windows, no admin rights, nothing installed):

```powershell
.\scripts\publish.ps1 -Zip
```

| Option | Result (measured, Phase 6) |
|---|---|
| default (framework-dependent) | **7.1 MB folder / 3.1 MB ZIP**; target machine needs the **.NET 8 Desktop Runtime (x64)** |
| `-SelfContained` | **168 MB folder**; **no** .NET needed on the target |

- Output: `publish\AusbildungsWegInterview-windows-x64\AusbildungsWegInterview.exe`
  (+ optional `.zip`). The `win-x64` artifact was produced and file-verified in
  this milestone (cross-published from the Linux build host); **launching it on
  Windows is your verification step** (see Status above).
- **AI models are not bundled.** After launch: set the whisper CLI path /
  download the speech model in Settings; pull the Ollama model; download any
  external-CLI MT models — all explicitly, all local.
- **Launch:** double-click `AusbildungsWegInterview.exe`.
- **Remove/uninstall:** delete the publish folder (or the ZIP). Per-user data
  lives in two places — delete both for a full removal:
  - `%APPDATA%\AusbildungsWegInterview\` (settings + local log),
  - `%LOCALAPPDATA%\AusbildungsWegInterview\models\` (explicitly downloaded
    whisper weights).
  No registry entries, no services, no background processes.
- **Final Windows verification:** after packaging, work through
  [`docs/Windows-Verification-Checklist.md`](docs/Windows-Verification-Checklist.md)
  on a real Windows 10/11 machine (the cross-published package is file-verified
  only; launch, WASAPI capture, and WPF rendering are Windows-only checks).
- The ZIP is reproducible: `scripts/publish.ps1 -Zip` (the build host's ZIP is a
  gitignored artifact, not committed — publish on your own machine to avoid any
  doubt about provenance).

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

175 tests run anywhere with the .NET 8 SDK (verified on Linux). The
whisper.cpp *real* tests (transcription, `-tr` translation, full-pipeline
end-to-end, corrupt model) additionally require a local whisper.cpp build +
`ggml-tiny.bin` + `jfk.wav` and self-report "SKIPPED" in the test output when
those are absent; all error-path tests run regardless.

Performance: `dotnet run --project benchmarks/AusbildungsWegInterview.Benchmarks`
(reproducible; results in [benchmarks/RESULTS.md](benchmarks/RESULTS.md)).

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
| "whisper translation supports English only" | You picked the whisper (→EN) engine with a non-English target — switch the engine to Ollama or External CLI. |
| "Pair de→fr is not covered by the configured model(s)" | The external CLI models don't include that pair — download the model for the pair (or use Ollama). |
| "External CLI needs a pinned source language" | Set the recognition language (Settings → Source language) when using the External CLI engine. |
| "Audio output device was disconnected" | The selected device was unplugged — reconnect it (or select another) and Start again. The pipeline auto-restarts once, then stops with this error. |
| Transcription is quiet/empty | The device may be muted or silent — loopback captures whatever is *playing*. |
| Restarting whisper settings has no effect | Stop listening first — the window blocks changes while capture runs. |

---

## Known limitations

- **Windows runtime not verified on the build host.** WASAPI capture, WPF UI at
  runtime, live device plug/unplug and the published win-x64 launch all require a
  Windows machine (see Status). Everything else — including the real whisper.cpp
  end-to-end pipeline — is verified in the automated suite.
- **Ollama latency not measured** on the build host (no Ollama installed); the
  README/benchmark docs label Ollama timings as estimates.
- Translation engine coverage is pair-dependent and reported honestly: whisper
  (→EN) covers any→EN; Argos/opus-mt covers the pairs you download; Ollama covers
  all pairs with a capable model. Unsupported pairs never pretend to translate.

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
- **Scanned PDFs**: the importer extracts embedded text only. Image-only
  (scanned) PDFs contain no text layer, so import reports "no extractable
  text" (no OCR is included, by the local-first/no-paid-dependency constraint).
  No structured-field extraction is claimed — the review box shows the raw
  extracted text, which you edit before grounding answers.
