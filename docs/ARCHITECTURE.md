# Architecture

AusbildungsWeg Interview is a modular, local-first desktop application. The
design goal is that **the UI never talks to a specific AI runtime directly**: it
depends only on abstractions, and concrete providers are wired together in a
single composition root.

## Module responsibilities

| Project | TFM | Role |
|---|---|---|
| `App` | `net8.0-windows` (WPF) | Floating window, view models, composition root. |
| `Core` | `net8.0` | Domain models, provider interfaces, the real-time pipeline, config model. |
| `Audio` | `net8.0-windows` | WASAPI loopback capture of system output (NAudio). |
| `Speech` | `net8.0` | whisper.cpp adapters: **CLI subprocess** (`WhisperCliRecognizer`, recommended) + native-library P/Invoke (`WhisperCppRecognizer`). Runtime-optional. |
| `Translation` | `net8.0` | Translation providers (Ollama LLM; explicit pass-through when disabled). |
| `Answers` | `net8.0` | Multilingual question detection, grounded candidate-context prompting, Ollama answer generator. |
| `Infrastructure` | `net8.0` | Local Ollama HTTP client, config store, whisper model manager, redacting logger, system info. |
| `Tests` | `net8.0` (xUnit) | Unit + integration tests (cross-platform). |

### Dependency direction

```
                       ┌─────────────┐
        App (WPF) ───► │    Core     │ ◄────────────────────────┐
                       │  (domain +  │                            │
                       │  pipeline)  │                            │
                       └──────┬──────┘                            │
        ┌──────────────┬──────┴───────┬──────────────┬────────────┴─────┐
        ▼              ▼              ▼              ▼                  ▼
    Audio(win)     Speech         Translation      Answers        Infrastructure
   (WASAPI)      (whisper.cpp)   (Ollama/pass-    (detect,        (Ollama client,
                              thru)        context,             config store,
                                                    generate)      model manager)
        └──────────────┴──────► all depend on Core ◄──────────────┘
```

- `Core` has **no external dependencies** and is fully cross-platform.
- `Audio` is Windows-only (WASAPI). Everything else is cross-platform.
- `Tests` reference only the cross-platform projects so the suite runs on any OS.

## Provider interfaces (the seams)

Defined in `Core`; implementations are swappable:

- `IAudioCapture` — loopback capture, frames, device list, errors.
- `ISpeechRecognizer` — stateful segment transcription: `PrepareAsync` (fail
  fast on missing exe/model), `ProcessAudioAsync` (feed a chunk, get the current
  best result), `FinalizeAsync` (force the final result at end-of-speech),
  `Reset` (clear segment state).
- `ITranslator` — translate a segment into a target language. Results carry an
  honest `StatusMessage` (unavailable / failed / pass-through / unchanged) so
  the UI never labels un-translated text as translated.
- `IAnswerGenerator` — produce a grounded answer suggestion; `IsAvailable`.
- `IQuestionDetector` — decide when a transcript is a usable question
  (multilingual, works on incomplete segments).

## Real-time pipeline

`Core.Pipeline.InterviewPipeline` owns the processing loop:

```
IAudioCapture.FrameCaptured
        │
        ▼
  bounded Channel<AudioFrame>   (capacity 128, DropOldest)
        │  worker task
        ▼
  PcmConverter.ToMono16k(frame)         downmix + resample to 16 kHz mono 16-bit
        │
        ▼
  AudioChunker.Push                      fixed-duration chunks (default 0.5 s)
        │  per chunk
        ▼
  SEGMENTATION (silence-driven):
    • segment START: first non-silent chunk (silence gates entry only)
    • while active: EVERY chunk (incl. quiet) is fed → sentence endings survive
    • segment END: recognizer returns a non-empty Final, OR sustained trailing
      silence (≥ SilenceFinalizeSeconds, default 0.8 s) → FinalizeAsync → reset
        │
        ▼
  ISpeechRecognizer.ProcessAudioAsync    partial / final transcript
        │
        ├──► TranscriptUpdated (shown immediately; provisional flag)
        ├──► ITranslator (deduped: only when text changed)  ──► TranslationUpdated
        └──► IQuestionDetector sufficient?
                    ├── provisional: answer ONCE per question  ──► AnswerUpdated (provisional)
                    └── final: always refine (+ remembered as recent context) ──► AnswerUpdated (final)
```

Note: an *empty* transcription result never ends a segment (whisper can
legitimately return nothing for a quiet chunk); only a non-empty final result or
sustained silence does.

### Concurrency, dedup, and cancellation

- **Bounded queue** with `DropOldest` prevents unbounded memory growth if the
  recognizer is slower than real time (backpressure: the oldest audio is
  dropped, `QueueDepth` is observable).
- **Single worker** reads frames; audio capture and inference never touch shared
  buffers from multiple threads.
- **Translation dedup:** a segment is re-translated only when its normalized text
  actually changed.
- **Answer policy:** a provisional answer is produced once a question is deemed
  sufficient, and is **not** regenerated on every minor transcript update; the
  final transcript produces the refined answer. **Stop cancels the in-flight
  generation** via the answer `CancellationTokenSource`.
- **Conversation context:** the last N final Q/A turns (default 3, truncated)
  are passed as bounded context so multi-turn interviews stay coherent.
- **Capture recovery:** a fatal device error triggers a **bounded** number of
  auto-restarts (`MaxCaptureRestarts`, default 1); beyond that the pipeline
  faults and stops cleanly with a visible error — never a tight retry loop.
- **Stop** unsubscribes from capture, completes the queue, joins the worker
  (which flushes the chunker tail and finalizes any active segment), cancels
  run/answer scopes, and releases the capture. Everything is `IAsyncDisposable`;
  repeated start/stop cycles are deterministic.

### State machine

`Idle → Preparing → Listening ⇄ Processing → Stopping → Stopped`, with a
terminal `Faulted` on unrecoverable errors. The UI mirrors this 1:1 and a
failed capture is reported as an error, never as "listening".

## Speech recognition (whisper.cpp)

Two adapters implement `ISpeechRecognizer`, selected in the composition root:

- **`WhisperCliRecognizer` (recommended):** drives a `whisper-cli` subprocess
  per inference. The segment buffer is written as 16-bit mono WAV, whisper runs
  with `-oj` (JSON output; timestamps are parsed and preserved), and results are
  labeled *Partial* (cadence: `PartialIntervalSeconds`) or *Final*
  (`FinalizeAsync`). Missing executable/model, incompatible binary, non-zero
  exit, malformed JSON, and timeouts each produce a distinct, clear error; the
  subprocess is killed on timeout/cancellation. The segment buffer is bounded
  (`maxSegmentSeconds`, default 30 s).
- **`WhisperCppRecognizer`:** P/Invoke into `libwhisper` (advanced; the shared
  library must be resolvable at runtime).

Both are honest about being **segment-based, not token-streaming**. Multilingual
models (DE/EN/FR/AR) are used; language pinning or auto-detection is configured
in the UI.

## Translation

- `OllamaTranslator`: LLM translation of the complete question via local Ollama.
  Honest status reporting: *unavailable*, *failed*, *no output*, and *returned
  unchanged* are all surfaced as `StatusMessage` and never labeled "translated".
- `PassThroughTranslator`: explicit mode when the user disables translation —
  the original is shown and labeled "pass-through".
- Source auto-detection: the recognizer's detected language is forwarded; a
  pinned source language overrides it.

## Threading model

- Audio capture raises `FrameCaptured` on the capture thread.
- The pipeline worker runs on a thread-pool task; provider calls (`whisper`,
  `Ollama`) may block that task but never the UI thread.
- The UI marshals all pipeline events back to the WPF `Dispatcher`.

## Grounding & honesty (answers)

`Answers.ContextManager` builds **bounded** prompts (CV ≤ 4000 chars, JD
≤ 3000 chars, recent turns truncated) that instruct the model to:
- answer **only** from the supplied CV / job description ("verified candidate
  material"),
- **never fabricate** qualifications, history, proficiency, achievements or
  experiences (STRICT HONESTY RULES),
- include **recent conversation context** (bounded) so multi-turn answers are
  coherent,
- write in the requested language, length (concise / normal / detailed) and
  **style** (concise / natural / professional), in a first-person spoken tone.

When the material doesn't support a claim, the model is told to give a neutral,
general answer or ask one short clarifying question.

## Question detection

`Answers.QuestionDetector` is a deterministic multilingual heuristic that works
on **incomplete** segments: `?`/`؟`, interrogative words and phrases for DE
(Warum, Weshalb, Wie, Was, Welche, Können Sie, Erzählen Sie, …), EN (Why, How,
What, Which, Could you, Tell me about, …), FR (Pourquoi, Comment, Qu'est-ce
que, Pouvez-vous, …), and AR (لماذا، كيف، ما، هل، ماذا). A provisional segment
needs an interrogative cue; a finalized segment only needs to be long enough.
Short fragments never trigger a final answer.

## Configuration & runtime rebuild

`Core.Config.AppConfiguration` holds no secrets. `Infrastructure.Config.
ConfigurationStore` persists it as local JSON (resilient to missing/corrupt
files → defaults). Key Phase 2 fields: `TranslationEnabled`, `AnswerStyle`,
`WhisperExecutablePath`, `WhisperModelPath`, `WhisperThreads`,
`PartialIntervalSeconds`, `OllamaRequestTimeoutSeconds`, `SilenceFinalizeSeconds`,
`RecentTurns`, `MaxCaptureRestarts`.

`App.AppServices` is the composition root. Because recognizer/translator choice
depends on config, `AppServices.RebuildPipeline()` rebuilds the speech
recognizer, translator, answer generator, and pipeline from the current config
(blocked while listening; candidate context preserved). Language/length/style
settings are read live by the pipeline and need no rebuild.

## Logging & system info

- `Infrastructure.Logging.RedactingAppLogger`: local structured log file
  (`%AppData%\AusbildungsWegInterview\logs\app.log`) + in-memory sink.
  **Defensively redacts** GitHub PATs, bearer tokens, and long opaque strings
  on every line; callers are additionally prohibited from logging transcript,
  CV, or audio content. Operational state only (state transitions, error
  types, backpressure).
- `Infrastructure.SystemInfo`: best-effort local probe (core count, physical
  RAM) used to make **conservative model suggestions** shown in the UI.
  Nothing is downloaded automatically.

## UI

- Floating tool window (topmost optional): status dot + text, explicit
  **"System audio is being captured (loopback) — microphone NOT used"**
  indicator (only while capture is genuinely active), Ollama + speech-engine
  setup status with actionable hints (exact `ollama pull` command, missing
  model, CLI path), RAM-based model suggestions.
- Transcript (original, provisional flag), **detected question**, translation
  (with honest status line), suggested answer (provisional/final).
- Settings: source language, translation on/off + target, answer
  language/length/style, speech model + download, whisper CLI path, model path,
  output device.
- A duplicate-operation guard prevents concurrent Start/Stop/settings-rebuild;
  async commands never block the UI thread; failed starts are reported as
  errors, never as "listening".

## Testing strategy

- **Unit tests** for pure logic: PCM conversion, chunking, silence gating,
  transcript normalization, multilingual question detection, context prompts,
  config defaults, model manager, Ollama HTTP client (stubbed/hanging HTTP),
  redacting logger, WAV writer, system info.
- **Integration tests** for the pipeline with mocked providers (deterministic
  scripted speech, mock translator/answer generator, scripted capture):
  provisional→final answer, translation dedup, silence-driven finalization,
  repeated start/stop, bounded capture auto-restart, backpressure, in-flight
  cancellation on Stop, graceful model failure.
- **Real whisper.cpp tests**: when a local whisper.cpp build + `ggml-tiny.bin`
  + `samples/jfk.wav` are present (auto-detected or via
  `AUSBILDUNGSWEG_WHISPER_CLI`), the suite runs **genuine subprocess
  transcriptions** (final + partial) and a corrupt-model failure; otherwise it
  reports SKIPPED in the test output. Error-path tests (missing exe/model,
  hanging executable timeout, WAV header) run everywhere.
- **Windows-only** verification (WASAPI capture, WPF window/ViewModel runtime)
  is deliberately kept out of the cross-platform suite and must happen on a
  Windows machine.

## Known limitations / next milestones

- Transcription is segment-based (partials on a cadence), not token-streaming.
  A `whisper_state`-based incremental decoder could reduce latency later.
- Dedicated local translation model comparison (latency/quality) vs. using the
  Ollama LLM for translation — the `ITranslator` seam allows dropping one in.
- WASAPI device-change hot-plug notification (currently: refresh-on-demand +
  removal surfaced as a capture error + bounded auto-restart).
- Hardware benchmarking (CPU/RAM/VRAM, latency) on representative Windows boxes.
- Packaging (MSIX/installer) with models kept separate from the installer.

---

# Phase 3 additions

## Device-change handling (WASAPI)

Windows exposes no simple managed "device changed" event for WASAPI
(`SystemEvents` has no DeviceChange; the Win32 path needs a hidden message
window). `LoopbackAudioCapture` therefore **polls device state every 2 s while
capturing** (endpoint enumeration is cheap):

- `Core.Audio.DeviceChangeMonitor` — pure, unit-tested decision logic:
  explicit selection lost → `DeviceLost` (fatal error → pipeline's bounded
  restart); default-follow mode + default moved → `RestartOnNewDefault`
  (capture re-opens internally, `DeviceChanged` raised); otherwise `None`.
- The pipeline logs device changes; the UI refreshes the device list and shows
  a status note while listening.
- Worst-case detection latency for a removed/changed device: ~2 s.

## Translation providers (Phase 3)

`ITranslator` implementations:

- `OllamaTranslator` — LLM translation, all pairs, optional (Phase 2).
- `WhisperTranslateAdapter` — wraps the recognizer's optional
  `ISpeechRecognizer`+`ISegmentTranslator` capability: whisper.cpp `-tr`
  translates the **segment audio** to **English only** (zero extra dependency,
  reuses the speech model). The pipeline runs it for **final** segments only
  (partials stay untranslated on this provider). Non-English targets and
  non-capable recognizers produce honest status, never a fake translation.
- `ExternalCliTranslator` — generic subprocess MT engine (opus-mt/mariannet
  class): text on stdin, first stdout line = translation; argument template
  with `{source}`/`{target}`; configured pair allow-list; timeouts; missing
  executable / unsupported pair / echo / timeout all reported honestly.
- `PassThroughTranslator` — explicit "off" mode, clearly labeled.

Provider selection: `AppConfiguration.TranslationProvider` (Ollama /
WhisperTranslate / ExternalCli / Off via the TranslationEnabled toggle) in the
composition root (`AppServices.CreateTranslator`), applied through
`RebuildPipeline()` when not listening.

### Translation evaluation evidence (Phase 3)

Measured on the build host (2 vCPU / 4 GB, CPU-only):
- whisper `-tr`: ~2.3 s per 11 s segment (tiny) — →EN only.
- Argos Translate (opus-mt DE→EN): 5.95 s cold, **0.16–0.36 s warm** per
  sentence; good quality; package index covers `de↔en`, `en↔ar` only (no
  `de↔fr`); Python runtime required on Windows → not bundled, only documented.
- NLLB-200 / M2M100: all pairs but ~2–3 GB models → impractical on modest
  hardware (documented, not integrated).
- Ollama LLM: all pairs with a ≥3B model, no new dependency for users who
  already run Ollama for answers (default engine).

## Answer-stage translation hand-off

The pipeline remembers the latest **genuine** translation of the current
segment (`IsTranslated` only — pass-through/unchanged/failure never qualify)
and passes it as `AnswerRequest.QuestionTranslated`, so the answer model sees
both the original and the translated question. The value is reset per segment
and per session.

## Orchestration & end-to-end tests

`Pipeline.FullPipelineOrchestrationTests` adds:
- Stop semantics: frames enqueued after Stop are **not** processed; capture
  stops match starts; final state is Stopped.
- State sequence well-formedness (Idle → … → Stopped).
- Device-change notification does not break a running pipeline.
- 1-minute sustained simulated audio: bounded, stable, clean stop.
- **REAL end-to-end** (gated on a local whisper.cpp build): actual JFK audio
  through the complete pipeline (real ASR + real question detection + mocked
  LLM stages) asserting the real transcript, a generated answer (final), and
  translation — clearly separate from the mocked deterministic tests.

## Performance benchmarks

`benchmarks/AusbildungsWegInterview.Benchmarks` (console, dev-only, not
shipped) measures on the running host: whisper transcription RTF, whisper `-tr`
latency, question-detection ns/op (median of 5), full-pipeline E2E timing
(provisional/final), and sustained-audio queue depth + peak RSS. Results with
host details: `benchmarks/RESULTS.md`. Windows/WPF/Ollama/GPU measurements are
explicitly marked as not runnable on the Linux build host.

## Release packaging

`scripts/publish.ps1` publishes `src/AusbildungsWegInterview.App` for `win-x64`
(Release). Framework-dependent (default, ~1 MB, needs .NET 8 Desktop Runtime)
or self-contained (`-SelfContained`, ~70–90 MB, no prerequisites). Optional
`-Zip`. No admin rights, no installer, no system changes; models excluded;
config in `%APPDATA%\AusbildungsWegInterview`. Removal = delete the publish
folder (+ optional appdata folder).
