# AusbildungsWeg Interview

**A local-first AI interview assistant for Windows.**

AusbildungsWeg Interview listens to the **interviewer's system audio** (Windows
Audio Loopback), transcribes the questions in real time with **whisper.cpp**,
translates them, and generates **context-aware answer suggestions** with a local
**Ollama** model — all on your own computer.

> **Purpose & ethics.** This tool supports interview *preparation* and permitted
> interview assistance. You are responsible for following the rules of your
> interview and for **disclosing AI assistance when required**. The app does not
> implement covert recording, hidden capture, or any feature designed to bypass
> interview restrictions.

---

## Status — Milestone 1 (foundation)

**Verified in this repository (cross-platform, automated):**
- The solution and all 8 projects build. The 5 platform-agnostic projects
  (Core, Infrastructure, Speech, Translation, Answers) compile cleanly.
- **75 unit + integration tests pass** (`dotnet test`), covering the real-time
  pipeline (chunking, silence gating, provisional→final answer, translation
  dedup, cancellation, graceful failure), PCM conversion, chunking, question
  detection, context prompting, the Ollama HTTP client, the config store, and the
  model manager.

**Requires a Windows machine (Windows 10/11) — not verifiable on a non-Windows build host:**
- The **WPF application** launching and the floating UI (`App`, `net8.0-windows`).
- **Windows Audio Loopback (WASAPI)** capture of a real output device (`Audio`).
- **whisper.cpp live transcription** against a real native build + model (`Speech`).
- End-to-end behavior with a real local Ollama server.

The Windows-only projects are complete source in this repository; they simply
cannot be compiled or run on a Linux/CI build host without the Windows Desktop
SDK. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Requirements

### To build & run the app (Windows)
- **Windows 10 or 11**
- **.NET 8.0 SDK** (or the .NET 8 Desktop Runtime to run a published build)
- (Optional) **Ollama** for answer suggestions: <https://ollama.com/download/windows>
- (Optional) **whisper.cpp** native library + a model for live transcription
  (see [Models](#models-downloads-explicit-local)).

### To run the automated tests (any OS with the .NET 8 SDK)
- `dotnet test` runs the full cross-platform suite on Windows, macOS, or Linux.

---

## Repository layout

```
AusbildungsWegInterview.sln
src/
  AusbildungsWegInterview.App/            WPF UI, view models, composition root (Windows)
  AusbildungsWegInterview.Audio/          WASAPI loopback capture (Windows)
  AusbildungsWegInterview.Core/           Domain models, provider interfaces, pipeline
  AusbildungsWegInterview.Speech/         whisper.cpp adapter (native, runtime-optional)
  AusbildungsWegInterview.Translation/    Translation providers (local/Ollama)
  AusbildungsWegInterview.Answers/        Question detection, context, answer generation
  AusbildungsWegInterview.Infrastructure/ Local Ollama client, config store, model manager
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

Or publish a self-contained Windows executable:

```powershell
dotnet publish src/AusbildungsWegInterview.App -c Release -r win-x64 --self-contained false
```

Model files are **not** included in the app or the build; download them
explicitly from within the app (Settings → Speech model → Download).

---

## Models (downloads: explicit, local)

- **Speech (whisper.cpp):** `tiny`, `base`, `small`, `medium` are available via the
  in-app **Download** button. Files are saved to your local user data folder
  (gitignored) and are **never committed** to Git. No model is downloaded at
  startup.
- **Answer/translation (Ollama):** uses a model already pulled on your local
  Ollama server (e.g. `ollama pull llama3.1:8b`). If Ollama isn't running, the
  app still transcribes and translates; it simply disables answer suggestions.

**Licensing:** whisper.cpp is MIT-licensed. The speech models are distributed by
ggerganov in the official whisper.cpp releases — review the applicable model
license before any use beyond personal interview preparation. Model binaries are
treated strictly as **data**, never executed as code.

---

## Privacy & security

- **No persistent audio** and **no persistent transcripts** by default.
- No server, no cloud, no subscription, **no API key** for the default workflow.
- **No Azure** is used or required; no cloud resources are created.
- Imported CV / job-description text is kept **local only** and never logged.
- A visible **listening indicator** and a reliable **Stop** control are always shown.
- `.gitignore` excludes model binaries, audio, transcripts, and any secrets.
- System-output (loopback) capture may include sound from **other applications**
  on the machine — the UI documents this.

---

## Testing

```powershell
dotnet test
```

Cross-platform tests run anywhere with the .NET 8 SDK. Windows-specific tests
(WASAPI capture, WPF UI, live whisper.cpp) must be run on a Windows machine and
are intentionally separate so that "source-code verified" is never conflated with
"runtime verified".

---

## Configuration

Settings are saved locally under your user AppData folder
(`AusbildungsWegInterview/config.json`). They contain **no secrets**: source
language, translation language, answer language, answer length, speech model,
Ollama model, output device, always-on-top, and pipeline tuning.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| "No active audio output device found" | Connect speakers/headphones, then **Refresh**. |
| "Speech model … download required" | Use **Download** in Settings, then Start. |
| "The native whisper runtime (libwhisper) is not available" | Build whisper.cpp and place the shared library next to the app. |
| "Local AI (Ollama): not running" | Start Ollama; answers are optional. Transcription/translation unaffected. |
| Transcription is quiet | The device may be muted, or another app is silent — loopback captures whatever is playing. |
