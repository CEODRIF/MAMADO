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
| `Speech` | `net8.0` | whisper.cpp adapter (P/Invoke). Native lib loaded at runtime; optional. |
| `Translation` | `net8.0` | Translation providers (local/Ollama; pass-through fallback). |
| `Answers` | `net8.0` | Question detection, candidate-context prompting, Ollama answer generator. |
| `Infrastructure` | `net8.0` | Local Ollama HTTP client, config store, whisper model manager. |
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
- `ISpeechRecognizer` — stateful partial/final transcription; `PrepareAsync`,
  `ProcessAudioAsync`, `Reset`.
- `ITranslator` — translate a segment into a target language.
- `IAnswerGenerator` — produce a grounded answer suggestion; `IsAvailable`.
- `IQuestionDetector` — decide when a transcript is a usable question.

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
  AudioChunker.Push                      fixed-duration chunks
        │  per chunk
        ▼
  SilenceDetector.IsSilent               skip quiet audio
        │
        ▼
  ISpeechRecognizer.ProcessAudioAsync    partial / final transcript
        │
        ├──► TranscriptUpdated (shown immediately; provisional flag)
        ├──► ITranslator (deduped: only when text changed)  ──► TranslationUpdated
        └──► IQuestionDetector sufficient?
                    ├── provisional: answer ONCE per question  ──► AnswerUpdated (provisional)
                    └── final: always refine                   ──► AnswerUpdated (final)
```

### Concurrency, dedup, and cancellation

- **Bounded queue** with `DropOldest` prevents unbounded memory growth if the
  recognizer is slower than real time.
- **Single worker** reads frames; audio capture and inference never touch shared
  buffers from multiple threads.
- **Translation dedup:** a segment is re-translated only when its normalized text
  actually changed.
- **Answer policy:** a provisional answer is produced once a question is deemed
  sufficient, and is **not** regenerated on every minor transcript update; the
  final transcript produces the refined answer. In-flight answer work for a
  superseded question is **cancelled** via a linked `CancellationTokenSource`.
- **Stop** completes the queue, joins the worker, cancels run/answer scopes, and
  disposes the capture. Everything is `IAsyncDisposable`.

### State machine

`Idle → Preparing → Listening ⇄ Processing → Stopping → Stopped`, with a
terminal `Faulted` on unrecoverable errors. The UI mirrors this 1:1.

## Threading model

- Audio capture raises `FrameCaptured` on the capture thread.
- The pipeline worker runs on a thread-pool task; provider calls (`whisper`,
  `Ollama`) may block that task but never the UI thread.
- The UI marshals all pipeline events back to the WPF `Dispatcher`.

## Grounding & honesty (answers)

`Answers.ContextManager` builds prompts that instruct the model to:
- answer **only** from the supplied CV / job description,
- **not fabricate** specifics when the material doesn't support a claim,
- write in the requested language at the requested length
  (concise / normal / detailed).

When the CV doesn't support a claim, the model is told to give a cautious,
general answer or ask a clarifying question.

## Configuration

`Core.Config.AppConfiguration` holds no secrets. `Infrastructure.Config.
ConfigurationStore` persists it as local JSON (resilient to missing/corrupt
files → defaults). Models are managed separately by
`Infrastructure.Models.WhisperModelManager` (explicit download, local dir, never
auto-downloaded at startup).

## Testing strategy

- **Unit tests** for pure logic: PCM conversion, chunking, silence gating,
  transcript normalization, question detection, context prompts, config store,
  model manager, Ollama HTTP client (stubbed HTTP).
- **Integration tests** for the pipeline with mocked providers (deterministic
  scripted speech, mock translator/answer generator): verifies provisional→final
  answer, translation dedup, silence skipping, cancellation, and graceful model
  failure.
- **Windows-only** tests (WASAPI, WPF, live whisper.cpp) are deliberately kept out
  of the cross-platform suite and must run on a Windows machine.

## Known limitations / next milestones

- Live whisper.cpp partial streaming (per-token updates) and explicit language
  pinning via the `whisper_state` API — Step 5.
- Dedicated local translation model comparison (latency/quality) vs. using the
  Ollama LLM for translation — Step 6.
- WASAPI device-change hot-plug notification (currently: refresh-on-demand +
  removal surfaced as a capture error) — Step 4 refinement.
- Packaging (MSIX/ installer) with models kept separate from the installer — Step 9.
- Hardware benchmarking (CPU/RAM/VRAM, latency) on representative Windows boxes — Step 8.
