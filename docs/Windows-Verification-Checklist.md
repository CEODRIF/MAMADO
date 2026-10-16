# Windows Final Verification Checklist

Run on a **Windows 10/11 x64** machine (fresh user profile recommended).
The package under test: `AusbildungsWegInterview-windows-x64\AusbildungsWegInterview.exe`
(see `publish/…-MANIFEST.txt`) or `scripts\publish.ps1 -Zip` rebuilt locally.

Legend:
- **[Auto]** — already covered by the automated suite (328 tests) on the Linux build host;
  listed here as context, no manual action needed.
- **[Win]** — can only be executed on Windows (runtime, WASAPI, WPF rendering).
- **[Manual]** — requires human judgment (readability, honesty of wording, UX feel).

## 1. First launch & startup failure recovery
- [ ] **[Win]** Double-click `AusbildungsWegInterview.exe` → window opens in < 5 s, no crash dialog, no admin prompt.
- [ ] **[Win]** First launch with no settings → UI language defaults to English, theme to light, status "Ready"; `config.json` created under `%APPDATA%\AusbildungsWegInterview\`.
- [ ] **[Win]** Start Listening with **no whisper CLI path** (default) → the app uses the **bundled native runtime** (whisper.dll + ggml*.dll next to the exe); with a downloaded model it transcribes, app stays usable either way.
- [ ] **[Win]** Start Listening with an invalid whisper path → localized error; fix the path; **Start again without relaunching** → works (failed-start retry).
- [ ] **[Auto]** Pipeline-level: failed start → Faulted → retry recovers; concurrent double-Start is a no-op (tests: `StartCaptureFailure_Faults_ThenRecovers_OnRetry`, `ConcurrentStart_SecondStartIsIgnored_NoDoubleSession`).

## 2. UI languages: English, German, French, Arabic (RTL)
- [ ] **[Win]** Switch UI language to each of de / fr / ar / en via Settings → **all** visible labels, statuses, and buttons switch without restart.
- [ ] **[Manual]** Arabic: layout mirrors correctly (RTL), no clipped/mixed-direction text in the transcript, translation, and answer panels; document review boxes remain RTL-readable.
- [ ] **[Win]** With an **error visible** (e.g. invalid whisper path) and a **translation status line** visible, switch language → both re-localize (no stale English/German text).
- [ ] **[Auto]** Catalog integrity: identical key sets en/de/fr/ar; no raw-English values; every status code localized (tests: `LocalizationTests`, `*StatusLocalizerTests`).

## 3. Light/dark themes & contrast
- [ ] **[Win]** Toggle both themes (Settings + header button) → all panels re-color, no white flash/white scroll area.
- [ ] **[Manual]** Contrast check in both themes: status text, disabled buttons, chips (provisional/refining), error text, disclaimer line, document state lines — all readable.
- [ ] **[Win]** Theme choice persists across restart (config round-trip).
- [ ] **[Auto]** Light/Dark define identical resource key sets; every referenced resource is defined (test: `ThemeResourceConsistencyTests`).

## 4. WASAPI system-output capture (real playback device)
- [ ] **[Win]** Play a known audio clip (e.g. a 30 s German news excerpt from a browser/Player) → **Start Listening** → transcript panel shows live partial text, then final segments.
- [ ] **[Win]** Status pill shows Listening (green) while capturing; audio from other apps is included (loopback) — expected behavior, confirm it doesn't crash.
- [ ] **[Auto]** Capture pipeline, chunking, silence-gated segmentation, backpressure (128-frame bounded queue, DropOldest) — covered by pipeline tests.

## 5. Microphone NOT captured by default
- [ ] **[Win]** While listening, check an audio-monitor tool (or mute your mic): **no microphone input** is opened by the app; only the selected **output** device's loopback is captured (Settings shows "Output (system audio)" devices only).
- [ ] **[Manual]** Confirm the UI makes clear what is being captured (system audio, not microphone).

## 6. Start/Stop, repeated clicks, device errors, restart after failure
- [ ] **[Win]** Spam-click **Start** (10× in a row) → exactly one session; spam-click **Stop** → clean single stop; buttons disable while busy (no double sessions).
- [ ] **[Win]** **Stop mid-transcription** (long clip playing) → in-flight whisper subprocess dies quickly (status → Stopped in < ~1 s), queued audio dropped, no error spam.
- [ ] **[Win]** Unplug/disable the capture output device mid-session → localized device error, capture auto-restarts once; second failure → stopped with a clear message.
- [ ] **[Win]** Change the Windows default output device while stopped → Refresh devices updates the list; while listening, the app recovers or reports (no hang).
- [ ] **[Auto]** Stop-cancels-in-flight (measured < 70 ms kill), re-entrant Stop safety, capture restart-once logic — covered by tests.

## 7. whisper.cpp configuration & real transcription
- [ ] **[Win]** **Default (native) mode:** published package contains `whisper.dll` + all `ggml*.dll` next to the exe; download a model (explicit **Download** button) → Start Listening → live transcription appears (no CLI path set).
- [ ] **[Win]** **Missing-runtime error:** rename `whisper.dll` away (app closed), Start Listening → **clear localized error** pointing to `scripts\build-whisper.ps1` (no crash, no silent empty transcript); restore the DLL → works again.
- [ ] **[Win]** Set the whisper CLI path (e.g. `main.exe` from a whisper.cpp build) + model `base` (use the in-app **Download** button — confirm it is explicit and progress is visible) → **Refresh status** shows "ready".
- [ ] **[Auto]** Pinned ABI: struct size/offset regression guard + real native P/Invoke JFK transcription (language detection + text) — `WhisperNativeAbiTests`, `WhisperNativeSmokeTests`; version mismatch → typed localized error, never undefined behavior.
- [ ] **[Win]** Transcribe a 10 s German + 10 s English clip → sensible transcripts in the panel (accuracy is model-dependent; judge usability, not perfect WER).
- [ ] **[Manual]** Confirm model download never happens at startup (fresh profile, no models dir → app runs, transcription shows "not configured", nothing downloads).

## 8. Question detection & translation
- [ ] **[Win]** Interviewer says (aloud or via TTS through system audio) a full question in German: *"Warum wollen Sie eine Ausbildung bei uns machen?"* → detected-question panel fills, translation appears in the configured language.
- [ ] **[Win]** Short fragments / statements (*"also", "gut", "und dann"*) → **no** answer is generated (detector ignores non-questions).
- [ ] **[Win]** Repeat the same question verbatim → answered once; not re-translated/re-answered (dedup).
- [ ] **[Win]** With translation engine set to Ollama **while Ollama is off** → honest localized status line ("Ollama not running…"), transcript still works.
- [ ] **[Auto]** Detector cues (de/en/fr/ar incl. unaccented ASR variants), dedup normalization (Arabic orthography), in-place refinement — covered by tests.

## 9. CV / JD import, review, activation, replacement, clearing
- [ ] **[Win]** Import a PDF CV, a DOCX, and a TXT (use your own or synthetic files — **no real personal data needed**) → extracted text appears in the review box; **answer grounding is NOT active yet** (state line "review").
- [ ] **[Win]** Press **Use** → state line "in use (N chars)", context status updates; ask a grounded question → answer uses the CV facts only.
- [ ] **[Win]** **Replace** the CV while in use → old grounding stays active until the new text is accepted (press Use again); **Clear** → state "not imported", grounding removed.
- [ ] **[Win]** Error paths: an empty .txt → "empty" error; a renamed .exe → "unsupported"; a > 10 MB file → "oversized"; a scanned (image-only) PDF → "no extractable text" (no OCR, by design).
- [ ] **[Manual]** Long CV text in the review box: scrolling/wrapping stays responsive, no UI freeze while importing (file read is off the UI thread).
- [ ] **[Auto]** Content sniffing, all error categories, review state machine, replace/clear semantics, no disk writes — covered by importer + review-state tests.

## 10. Ollama: missing / running / missing model / answer / timeout / recovery
- [ ] **[Win]** Ollama **not installed/running** → localized "Ollama not running" line in the answer panel; transcription + translation (if configured) keep working.
- [ ] **[Win]** Ollama running, model **not pulled** → localized "model '{name}' not installed — run `ollama pull …`" line.
- [ ] **[Win]** Correct model pulled → ask a question → **final answer suggestion** appears with the review disclaimer; CEFR level + answer language respected (set B1/German and judge the text).
- [ ] **[Win]** Slow model / large context (timeout path) → localized "timed out" status, not a silent freeze (default 60 s, configurable).
- [ ] **[Win]** **Start Ollama mid-session** (it was off at first question) → next question recovers without restarting the app (re-probe, 20 s window).
- [ ] **[Auto]** All six status codes, timeout-vs-stop distinction, re-probe logic, anti-fabrication + injection guard in the prompt — covered by generator/client/pipeline tests with fakes.

## 11. Cancellation & no results after Stop
- [ ] **[Win]** Ask a question, then **Stop before the model responds** → no answer appears afterwards; panel keeps its pre-Stop content; no crash/error spam.
- [ ] **[Manual]** After Stop, the app never shows a "new" answer or translation for audio captured after the Stop click.

## 12. Copy button / clipboard
- [ ] **[Win]** Copy button enabled **only** for a real suggestion (disabled for status lines); clicking copies the full answer to the clipboard (paste into Notepad to verify); with the clipboard locked by another app → localized error, no crash.

## 13. Privacy checks (no default persistence)
- [ ] **[Win]** Run a full session (transcription + CV/JD import + answers), then exit. Inspect:
  - `%APPDATA%\AusbildungsWegInterview\` → **only** `config.json` (+ `logs\app.log`); no audio, no transcripts, no CV/JD text, no answers.
  - `%LOCALAPPDATA%\AusbildungsWegInterview\models\` → only explicitly downloaded whisper weights.
- [ ] **[Manual]** Open `logs\app.log` → operational state only (state transitions, error *types*); no transcript text, no CV content, no local file paths from error messages.
- [ ] **[Auto]** Config carries no personal-data properties (reflection test); persisted JSON scan; redacting logger tests; document import directory snapshot/diff (no disk writes).

## 14. Clean exit & restart
- [ ] **[Win]** Close the window (X) → process exits within ~2 s (`tasklist | findstr AusbildungsWeg` shows nothing); no leftover whisper/ollama subprocesses started by the app.
- [ ] **[Win]** Relaunch → settings, language, theme, device, and model paths restored from `config.json`; imported CV/JD are **gone** (in-memory by design) and must be re-imported.
- [ ] **[Manual]** Repeat launch/exit 5× → no growing memory, no lock files, no "config corrupt" prompt.

## Sign-off
| Area | Result (pass/fail/n.a.) | Notes |
|---|---|---|
| 1–14 | | |

**Release readiness rule:** the app is only "release-ready" after every [Win]/[Manual]
item above is passed on a real Windows 10/11 machine. [Auto] items have evidence from
the Linux build host (see `README.md` → Testing) and do not require re-testing.
