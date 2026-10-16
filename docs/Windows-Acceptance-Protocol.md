# Windows Acceptance Protocol — AusbildungsWeg Interview

**Purpose:** single, reproducible, copy-paste acceptance session on a real
Windows 10/11 x64 machine. Consolidates `Windows-Verification-Checklist.md`
(kept for history) with the current implementation, including the Phase 2
first-run diagnostics and the accessibility pass (localized names, live-region
screen-reader announcements, themed keyboard focus).

**Target commit:** the commit where Phases 2–3 are merged into `main`.
Until that merge is authorized and pushed, there is nothing new to check out
on a second machine — do not run this protocol against `main` yet.

**Rules honored by this protocol:** no paid APIs, no cloud services, no
automatic model downloads, no admin rights, no system-wide installs beyond
the explicitly listed dev tools.

---

## 0. Automated vs. Windows-executed verification (read first)

Verification is split into two worlds. Only the second one needs this session.

| World | Where | What | Evidence already collected |
|---|---|---|---|
| **A — Automated (done, re-runnable anywhere)** | Linux build host (any OS with .NET 8 SDK) | Full unit + integration suite (**367 tests** as of this protocol): pinned whisper.cpp ABI (struct size/offsets, real P/Invoke JFK transcription when a local build is present), pipeline (chunking, silence gating, backpressure, stop-cancels-in-flight, restart-once), multilingual question detection, translation honesty, grounded answers, Ollama client, redacting logger, config, **localization key-set identity EN/DE/FR/AR**, **theme key parity**, **accessibility guards** (a11y names, focus visual in both themes, a11y keys in all 4 locales, live-region wiring) | `dotnet test -c Release` → 367/367; `dotnet build -c Release --no-incremental` → 0 warnings / 0 errors; `dotnet format --verify-no-changes` → clean |
| **B — Windows execution (this session)** | Windows 10/11 x64 | WPF launch, WASAPI loopback capture, live Narrator/keyboard behavior, real device errors, real Ollama interaction, package contents | collected during this session (Phase E below) |

Items marked **[A]** below need no manual action — they exist as context.
Items marked **[B]** are executed here, each with a pass/fail criterion and
the evidence to collect.

> The Linux host **cannot** verify: WPF rendering, WASAPI, real device
> failures, Narrator announcements, the packaged executable, or that a
> Windows-native MinGW build of whisper.cpp behaves identically to the
> cross-compiled one. Nothing in the automated world is claimed to cover
> these.

**Evidence convention:** create `acceptance/evidence/` next to the publish
folder. Screenshot each [B] pass as `E<check-id>.png` (e.g. `E4-2.png`);
save short console output as `E<check-id>.txt`. A check passes only with its
evidence file (or the stated exception).

---

## Phase A — Prerequisites and installation (Windows machine)

Fresh user profile recommended. All commands run in **PowerShell** (no admin
needed; winget per-user scope is fine).

### A.1 Install (or verify) the toolchain

```powershell
# .NET 8 SDK
winget install --id Microsoft.DotNet.SDK.8 -e
# Git for Windows
winget install --id Git.Git -e
# CMake
winget install --id Kitware.CMake -e
# MSYS2 (provides the MinGW-w64 x64 toolchain)
winget install --id MSYS2.MSYS2 -e
```

Then, **inside the MSYS2 terminal** (`msys2.exe` from the Start menu):

```sh
pacman -S --noconfirm mingw-w64-x86_64-gcc
```

That package provides `g++` **and** `mingw32-make`.

> **Important:** close and reopen PowerShell after the installs so the new
> PATH entries apply. The MinGW-w64 binaries live in
> `C:\msys64\mingw64\bin` — if the preflight in Phase B.2 still reports
> `g++` or `mingw32-make` missing, add that directory to the session PATH:
>
> ```powershell
> $env:PATH = "C:\msys64\mingw64\bin;" + $env:PATH
> ```

Optional (only for answer generation / Ollama translation):

```powershell
winget install --id Ollama.Ollama -e   # or: https://ollama.com/download/windows
ollama pull llama3.1:8b                # default model the app probes
```

### A.2 Verify every tool is visible

```powershell
dotnet --version     # 8.0.x
git --version
cmake --version
g++ --version        # "x86_64-*-w64-mingw32-g++"
mingw32-make --version
```

**Pass criterion [B A-2]:** all five print versions (g++ identifies as
mingw32). **Evidence:** `E_A-2.txt` (paste of the five outputs).

---

## Phase B — Source, pinned Whisper runtime, automated verification

### B.1 Get the code

```powershell
git clone https://github.com/CEODRIF/MAMADO.git
cd MAMADO
git checkout main            # the merged Phase 2–3 commit (see header)
git log -1 --oneline         # record the SHA under test
```

**Evidence:** `E_B-1.txt` with the SHA.

### B.2 Build the pinned native runtime (Windows-native build)

```powershell
.\scripts\build-whisper.ps1
```

What it does: preflight (git, cmake, g++, mingw32-make) → clones whisper.cpp
into `..\tools\whisper.cpp` once → checks out the **pinned commit
`0c7be696371069e0baa2515ed7aa08d5a5473aa6`** (libwhisper 1.9.5, ggml 0.26.0 —
the exact ABI the C# P/Invoke mirrors were compiled against) → CMake Release
shared build → stages the full DLL set into `native\win-x64`.

**Pass criterion [B B-2]:** exit code 0; `native\win-x64` contains exactly
the expected set (whisper lib + 3 ggml + 4 MinGW runtime DLLs):

```powershell
Get-ChildItem native\win-x64\*.dll | Select-Object Name, Length
```

Expected names: `libwhisper.dll` (or `whisper.dll`), `ggml.dll`,
`ggml-base.dll`, `ggml-cpu.dll`, `libgcc_s_seh-1.dll`, `libstdc++-6.dll`,
`libwinpthread-1.dll`, `libgomp-1.dll`.
**Evidence:** `E_B-2.txt` (script tail) + `E_B-2.png` (dir listing).

### B.3 Automated suite on Windows (sanity parity with the Linux host)

```powershell
dotnet test  AusbildungsWegInterview.sln -c Release
dotnet build AusbildungsWegInterview.sln -c Release --no-incremental
dotnet format AusbildungsWegInterview.sln --verify-no-changes
```

**Pass criterion [B B-3]:** tests — 0 failed (expect 367; with the
`tools\whisper.cpp` build from B.2 present, the whisper integration tests run
for real instead of self-skipping — count the `SKIPPED` lines and record
them); build — 0 warnings / 0 errors; format — no output.
**Evidence:** `E_B-3.txt` (three tails).

---

## Phase C — Produce the distribution package

```powershell
.\scripts\publish.ps1 -Zip
```

**Pass criterion [B C-1]:** `publish\AusbildungsWegInterview-windows-x64\`
contains the executable **and the complete native runtime set**, and a ZIP is
produced. Verify with:

```powershell
$dir  = "publish\AusbildungsWegInterview-windows-x64"
$need = @("ggml.dll","ggml-base.dll","ggml-cpu.dll",
         "libgcc_s_seh-1.dll","libstdc++-6.dll","libwinpthread-1.dll","libgomp-1.dll")
$miss = @($need | Where-Object { -not (Test-Path (Join-Path $dir $_)) })
if (-not (Test-Path (Join-Path $dir "AusbildungsWegInterview.exe"))) { $miss += "AusbildungsWegInterview.exe" }
if (-not ((Test-Path (Join-Path $dir "libwhisper.dll")) -or (Test-Path (Join-Path $dir "whisper.dll")))) { $miss += "libwhisper.dll/whisper.dll" }
if ($miss.Count) { "MISSING: $($miss -join ', ')" } else { "package complete" }
[math]::Round((Get-Item "publish\AusbildungsWegInterview-windows-x64.zip").Length / 1MB, 1)
```

Record the ZIP size (cross-compiled reference on the Linux host: ~13 MB; a
Windows-native build may differ by a few MB — record the actual value).
**Evidence:** `E_C-1.txt`.

From here on, **test the published package** (double-click launch), not
`dotnet run`.

---

## Phase D — Application acceptance checks

For every check: perform the action, compare with the expected result, save
evidence per the convention in §0. A single failed [B] check blocks release
readiness (see sign-off).

### D.1 Startup

| ID | Action | Expected result (pass criterion) | Evidence |
|---|---|---|---|
| E1-1 | Double-click `AusbildungsWegInterview.exe` (fresh profile: no `%APPDATA%\AusbildungsWegInterview` yet) | Window opens < 5 s; no crash dialog; **no UAC/admin prompt** | screenshot of first paint |
| E1-2 | Inspect `%APPDATA%\AusbildungsWegInterview\` after launch | `config.json` created; defaults = English UI, light theme, status "Ready" | screenshot of config.json |
| E1-3 | Relaunch immediately | Restarts cleanly, no "config corrupt" prompt | note in E1-3.txt |

### D.2 First-run diagnostics (Phase 2)

The Setup card lists exactly which prerequisites are missing, with the
concrete next step per line. Required lines: **native runtime**, **speech
model**. Optional line: **Ollama** (absent ⇒ "optional unavailable", never
blocks).

| ID | Setup (state before action) | Action | Expected | Evidence |
|---|---|---|---|---|
| E2-1 | runtime present (bundled), no model, Ollama off | Launch | Setup card shows: runtime **OK (with version)**; model **missing** + download instruction; Ollama line optional/not-installed. Card does **not** block the UI | screenshot |
| E2-2 | as E2-1 | Download the model (explicit button, Settings) | Download is user-initiated, progress visible; card line flips to OK; **card disappears** once all required items are OK | screenshot after |
| E2-3 | with Ollama running + model pulled | Press the header **Refresh** button | Ollama line flips to OK; no app restart needed | screenshot |
| E2-4 | negative control: rename `libwhisper.dll` in the publish dir (app closed) | Launch + Refresh | Runtime line shows **missing** with the exact script to run (`scripts\build-whisper.ps1`); app remains fully usable otherwise | screenshot, then restore the DLL |

### D.3 WASAPI loopback capture (real device)

| ID | Action | Expected | Evidence |
|---|---|---|---|
| E3-1 | Play a known ~30 s audio clip on the selected output device (browser/player), then **Start Listening** | "System audio is being captured (loopback…)" indicator appears; status pill = Listening; transcript panel shows **partial** text that finalizes into segments | screenshot mid-session |
| E3-2 | While listening, check the device list in Settings | Only **output (playback)** devices are listed — no microphone entry anywhere in the app | screenshot |
| E3-3 | Negative control: mute/disable the microphone while the session runs | Transcription continues (proves capture is loopback of the output, not the mic) | note |
| E3-4 | Play audio from a second app during the session | Included in the transcript (documented loopback behavior), no crash | note |

### D.4 Transcription (native, bundled runtime)

| ID | Action | Expected | Evidence |
|---|---|---|---|
| E4-1 | Whisper CLI path **empty** (default) | App uses the bundled native runtime (no path configuration needed) | config.json excerpt |
| E4-2 | Play 10 s German + 10 s English speech (TTS or recorded clip) | Sensible transcripts in both languages (judge usability, not perfect accuracy); language handling per the configured interview language / auto-detect | screenshot of both segments |
| E4-3 | Fresh profile, no model downloaded | Transcription shows the localized "model not configured" state; **nothing is downloaded automatically at startup** | screenshot |
| E4-4 | Corrupt/foreign runtime: replace `libwhisper.dll` with a wrong-version library (if available) or skip | Version mismatch ⇒ clear localized error, app never runs with an incompatible ABI | screenshot (or mark n.a. with reason) |

### D.5 Translation

| ID | Setup | Action | Expected | Evidence |
|---|---|---|---|---|
| E5-1 | engine = Ollama, Ollama **off** | Ask a question | Honest localized status ("Ollama not running…"); **never** a faked translation; transcript still works | screenshot |
| E5-2 | engine = Ollama, Ollama on + model pulled | Ask a German question with translation language EN | Translation appears, labeled as translated | screenshot |
| E5-3 | engine = whisper.cpp | Same question | Works only into **English**; any other target reports the honest "use Ollama" status | screenshot |
| E5-4 | engine = Off | Same question | Original shown, clearly labeled **pass-through** | screenshot |
| E5-5 | any engine | Repeat the identical question verbatim | Answered/translated **once** — dedup, no duplicate re-translation | note |

### D.6 Answer generation

| ID | Setup | Action | Expected | Evidence |
|---|---|---|---|---|
| E6-1 | Ollama on; answer language DE, level **B1** | Interviewer asks (TTS through system audio): *„Warum wollen Sie eine Ausbildung bei uns machen?"* | Detected-question panel fills; **suggested answer** appears with the review disclaimer; text plausibly at B1 German | screenshot |
| E6-2 | same | Say only fragments/statements (*„also", „gut", „und dann"*) | **No** answer is generated | note |
| E6-3 | import a CV (PDF/DOCX/TXT — synthetic, no real personal data) | Review box shows extracted text, state "review"; press **Use** → state "in use (N chars)"; ask a grounded question | Answer uses **CV facts only** (no fabricated qualifications) | screenshot |
| E6-4 | CV in use | Replace the CV | Old grounding stays active until the new text is accepted; **Clear** → "not imported" | screenshot |
| E6-5 | importer errors | empty .txt / renamed .exe / >10 MB file / scanned image-only PDF | Localized errors: empty / unsupported / oversized / no extractable text (no OCR by design) | screenshot per case (or one collage) |
| E6-6 | Ollama on | Ask a question, then **Stop before the model responds** | No answer appears afterwards; panel keeps pre-Stop content; no error spam | note |

### D.7 Overlay (compact interview mode)

| ID | Action | Expected | Evidence |
|---|---|---|---|
| E7-1 | During a live session, switch to the compact interview overlay | Compact window ~420×300 appears at the **bottom-right of the work area** (multi-monitor: correct monitor), 300 ms eased transition (unless reduced motion); transcript/translation/answer visible | screenshot |
| E7-2 | Drag the compact header; move it off-screen | Window stays inside the work area (clamped); drag works from the header area only | note |
| E7-3 | Resize by the edges | Bounded to 340×240 – 720×520, never smaller/larger | screenshot at min size |
| E7-4 | Exit back to main mode | Original main window size/position restored exactly (not clobbered) | screenshot |
| E7-5 | Windows setting "Show animations" **off** (reduced motion) | Transition is instant (no easing), everything else identical | note |

### D.8 Keyboard navigation & visible focus

| ID | Action | Expected | Evidence |
|---|---|---|---|
| E8-1 | From the window, press **Tab** repeatedly until you return to the start (full cycle) | Every interactive control (4 language combos, Start, Stop, theme toggle, Refresh, output device + refresh, model + download, all 7 path text boxes, translation engine, external CLI pair, Ollama model, length, style, CV/JD buttons, Copy) is reachable **in a sensible order**; no dead Tab stop, no unreachable control | video or step screenshots; record the full Tab order in E8-1.txt |
| E8-2 | Operate everything **without the mouse**: Space/Enter on buttons, **F4/Space** to open a ComboBox + arrows to select, arrows/typing in TextBoxes, Space to toggle the Advanced-settings Expander | All actions complete keyboard-only | video strongly recommended |
| E8-3 | With focus on controls in **light and dark** themes | The themed focus ring (`AwFocusVisual`, luminous rounded outline) is clearly visible on every focused control in both themes | one screenshot per theme |
| E8-4 | Start/Stop via keyboard while busy | Buttons disable while busy; Enter on a disabled Start does nothing (no double session) | note |

### D.9 Narrator / screen-reader (accessibility pass)

Launch Narrator: **Win + Ctrl + Enter**. (Narrator is sufficient for
acceptance; NVDA is a welcome extra but not required.)

| ID | Action | Expected (pass criterion) | Evidence |
|---|---|---|---|
| E9-1 | Tab to the interview-language ComboBox and open it | Narrator announces a **meaningful, localized** name (e.g. "Interview language" / the current locale's label) — not "ComboBox 12" | record what is announced (text or voice memo) |
| E9-2 | Tab to the Whisper model selector and the Translation engine selector | Announce the dedicated labels **"Whisper speech model"** / **"Translation engine"** (localized) | as E9-1 |
| E9-3 | Tab to the 7 advanced path text boxes | Each announces a meaningful name (paths for Whisper CLI, model, external CLI, CLI args, Ollama model, CV, job description) — no unnamed boxes | as E9-1 |
| E9-4 | Start a live session; let the transcript update | **New transcript text is announced** as it changes (live region) | voice memo / note of the announcement |
| E9-5 | Same for a translation change and an answer change | Each new content change is announced | as E9-4 |
| E9-6 | Repeat E9-4/E9-5 **in compact overlay mode** | Announcements continue (active-mode element is announced) | note |
| E9-7 | Tab to the status chip | Announces the current **status text** (bound live), e.g. listening/stopped | note |
| E9-8 | Switch UI language to **Arabic** with Narrator on | Announcements switch to the Arabic labels | note |

### D.10 Languages & Arabic RTL

| ID | Action | Expected | Evidence |
|---|---|---|---|
| E10-1 | Cycle UI language EN → DE → FR → AR (Settings/header) | **All** visible labels, statuses, buttons re-localize without restart | one screenshot per language |
| E10-2 | Arabic active: inspect full window | Layout mirrored RTL; no clipped or mixed-direction text in transcript/translation/answer panels, document review boxes, chips | screenshot |
| E10-3 | With an **error visible** + a translation status line, switch language | Both re-localize (no stale text from another locale) | screenshot |

### D.11 Error recovery

| ID | Failure induced | Expected recovery | Evidence |
|---|---|---|---|
| E11-1 | Invalid Whisper CLI path set → Start | Localized error; fix the path; **Start again without relaunching** → works (failed-start retry) | screenshots before/after |
| E11-2 | Rename the bundled `libwhisper.dll` away (app closed) → launch → Start | Clear localized error naming `scripts\build-whisper.ps1`; no crash, no silent empty transcript; restore DLL → works | screenshots |
| E11-3 | Unplug/disable the capture output device mid-session | Localized device error, capture auto-restarts **once**; second failure → stopped with a clear message (no hang) | note + screenshot |
| E11-4 | Change the Windows default output device while stopped | **Refresh** updates the device list; selecting works | screenshot |
| E11-5 | Ollama off at first question, started mid-session | Next question recovers without app restart (re-probe within the 20 s window) | note |
| E11-6 | Spam-click Start 10×, Stop 10× | Exactly one session; clean single stop; no double sessions, no error spam | note |
| E11-7 | Stop mid-transcription (long clip playing) | Status → Stopped within **~1 s**; queued audio dropped; no further transcript updates | timed note |

### D.12 Privacy & clean exit

| ID | Action | Expected | Evidence |
|---|---|---|---|
| E12-1 | Run a full session (transcription + CV/JD import + answers), then exit | `%APPDATA%\AusbildungsWegInterview\` contains **only** `config.json` (+ `logs\app.log`); **no audio, no transcripts, no CV/JD text, no answers**; `logs\app.log` holds operational state only (no transcript text, no file paths) | screenshot of folder + log excerpt |
| E12-2 | `%LOCALAPPDATA%\AusbildungsWegInterview\models\` | Only explicitly downloaded whisper weights | screenshot |
| E12-3 | Close via X | Process exits within ~2 s (`tasklist | findstr AusbildungsWeg` empty); no leftover app-spawned subprocesses | E12-3.txt |
| E12-4 | Relaunch | Settings/language/theme/device/model paths restored; imported CV/JD **gone** (in-memory by design) | screenshot |
| E12-5 | Repeat launch/exit 5× | No growing memory (Task Manager note), no lock files, no corrupt-config prompt | note |

---

## Phase E — Recovery quick reference

### E.r1 Missing native runtime / missing DLLs
Symptom: Setup card runtime line = missing, or Start shows the localized
runtime error (names `scripts\build-whisper.ps1`); possibly Windows "DLL not
found" for a `ggml*.dll` or MinGW runtime DLL.

```powershell
# If native\win-x64 already exists (built earlier):
copy native\win-x64\*.dll publish\AusbildungsWegInterview-windows-x64\
# Otherwise rebuild and re-publish:
.\scripts\build-whisper.ps1
.\scripts\publish.ps1 -Zip
```

Check the **full set of 8 DLLs** is present (the publish script itself warns
on a partial copy — read the warning, it names the missing files).

### E.r2 Missing speech model
- In-app: Settings → model size (`tiny`/`base`/`small`/`medium`) → **Download**
  (explicit only; progress visible). Saved to
  `%LOCALAPPDATA%\AusbildungsWegInterview\models\`.
- Manual: copy any `ggml-*.bin` (multilingual, no `.en`) into that folder and
  set **Model path**, then Refresh.

### E.r3 Ollama missing / not running / model absent
- Install: `winget install --id Ollama.Ollama -e` (or the official installer).
- Server: start Ollama (system tray app; or `ollama serve` in a terminal).
- Model: `ollama pull llama3.1:8b` (the default the app probes; the app shows
  the exact pull command when the configured model is missing).
- Until then the app must keep working — transcription/whisper-translation
  are unaffected; answer panel shows the localized "Ollama not running" state.

### E.r4 Native build failures (`build-whisper.ps1`)
| Symptom | Cause | Fix |
|---|---|---|
| Preflight: "Missing tool(s) on PATH: g++ / mingw32-make" | MSYS2 not installed or PATH not refreshed | Install §A.1, **reopen PowerShell**, and/or `$env:PATH = "C:\msys64\mingw64\bin;" + $env:PATH` |
| "cmake configure failed … mismatched generator" | Stale `build` dir from another toolchain | The error names the exact path — delete `tools\whisper.cpp\build` and re-run |
| `git clone`/checkout fails | Network / repo moved | The pinned SHA `0c7be69…` must exist upstream; retry, or pass `-SourceDir` with an existing checkout |
| Build succeeds but staging warns a MinGW runtime DLL is missing | Toolchain dir not next to g++ | The NOTE line names the DLL + source dir; copy it into `native\win-x64`, re-publish |
| App says version mismatch after a successful build | A different whisper.cpp was built | Re-run the script (it forces the pinned SHA); the app must reject mismatched runtimes — do not "fix" by bypassing the guard |

---

## Phase F — Distribution strategy recommendation (decision required, nothing bundled)

Two viable strategies for the pinned Windows x64 whisper runtime:

| Criterion | **Option A — Prebuilt runtime shipped in the package** | **Option B — Build-on-machine** |
|---|---|---|
| Reproducibility | Exact pinned commit ⇒ same source every time; the *toolchain* of the build machine is a hidden variable unless the artifact is hash-pinned. Mitigation: `publish.ps1` can emit a SHA256 manifest (small follow-up) | Fully deterministic on each machine (source + local toolchain + pin) |
| Binary compatibility | The current artifact was **cross-compiled (MinGW on Linux)**: exports verified, but *not* exercised on Windows — a Windows-native rebuild removes that doubt. MinGW DLLs run on stock Windows (no VC++ redistributable needed) only when the 4 runtime DLLs are bundled — the script already does this | Native MSYS2 build; known-good on that machine class |
| Maintenance | One-time build cost per pin bump; artifact must be regenerated and re-distributed on every whisper.cpp pin change | Every new user pays the build cost (minutes + ~1 GB toolchain) per machine |
| Clean-machine setup | **Trivial**: unzip, run. Only the .NET 8 Desktop Runtime is required (or `-SelfContained` to remove even that) | Heavy: Git + CMake + MSYS2 + mingw-w64-gcc, PATH pitfalls, ~10–15 min build |
| Security | Users run third-party-built binaries without rebuilding — acceptable for a private/internal release; mitigated by pin + SHA256 manifest + private repo distribution | Users compile from the pinned, inspectable source with their own toolchain — strongest trust story |

**Recommendation: hybrid (A-first with B as the verified source of truth).**

1. **For this acceptance session:** build the runtime **Windows-native**
   (Phase B.2) — this both validates the script on its real target and yields
   the reference artifact.
2. **For distribution:** `publish.ps1 -Zip` already bundles the runtime from
   `native\win-x64`; the supported artifact remains the **ZIP with the
   runtime inside** (Option A), produced from a Windows-native build.
   No installer for v1 (out of scope, accepted).
3. **Keep Option B available** (`build-whisper.ps1`) as the repair/rebuild
   path and for the hash-pin story.
4. **Small hardening follow-up (optional, approval-gated):** have
   `publish.ps1` write a `SHA256SUMS` manifest next to the ZIP so recipients
   can verify the exact runtime they received.
5. **Do not commit binaries to the repository** under either option
   (`native\` stays gitignored); the repo remains source + scripts only.

Rationale in one line: clean-machine setup is the dominant user-experience
factor for a Windows audience that is not necessarily technical (apprentices /
trainees), so they get the ZIP; the pin + native reference build + optional
hash manifest keep the trust and maintenance story intact.

*Nothing is bundled, published, or uploaded by this document — this section
is a recommendation awaiting your decision.*

---

## Sign-off

| Area | Result (pass / fail / n.a.) | Evidence files | Notes |
|---|---|---|---|
| A — toolchain (A-2) | | | |
| B — source/runtime/auto (B-1…B-3) | | | |
| C — package (C-1) | | | |
| D.1 startup (E1-1…3) | | | |
| D.2 first-run diagnostics (E2-1…4) | | | |
| D.3 WASAPI loopback (E3-1…4) | | | |
| D.4 transcription (E4-1…4) | | | |
| D.5 translation (E5-1…5) | | | |
| D.6 answers (E6-1…6) | | | |
| D.7 overlay (E7-1…5) | | | |
| D.8 keyboard & focus (E8-1…4) | | | |
| D.9 Narrator (E9-1…8) | | | |
| D.10 languages/RTL (E10-1…3) | | | |
| D.11 error recovery (E11-1…7) | | | |
| D.12 privacy/exit (E12-1…5) | | | |
| F — distribution decision | | | approved option + notes |

**Release readiness rule:** the product is release-ready only when every
[B] check above is *pass* (or *n.a.* with a recorded, justified reason) on a
real Windows 10/11 x64 machine, **and** the automated suite is green on the
same commit, **and** the distribution decision (Phase F) is made. All [A]
evidence is inherited from the Linux build host runs recorded in the commit
history — no re-testing required, but re-run B.3 for on-site parity.

**Do not claim "product complete" before this sign-off is filled in.**
