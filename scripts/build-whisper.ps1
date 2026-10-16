# -----------------------------------------------------------------------------
# AusbildungsWeg Interview — pinned whisper.cpp native runtime (Windows x64).
#
# Builds the EXACT whisper.cpp commit the app was compiled against (the C#
# P/Invoke mirrors in WhisperNative.cs are pinned to this ABI; the app also
# refuses to run a runtime whose whisper_version() does not match).
#
# Usage (from anywhere, in PowerShell):
#   .\scripts\build-whisper.ps1                     # build into repo native\win-x64
#   .\scripts\build-whisper.ps1 -SourceDir <path>   # reuse an existing checkout
#
# Requirements: git, CMake, and a MinGW-w64 x64 toolchain (e.g. MSYS2 mingw64
# with g++ in PATH). The output is copied next to the app at publish time.
# No admin rights; nothing is installed system-wide; fully offline after the
# one-time source fetch.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string]$SourceDir = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

# ---- pin --------------------------------------------------------------------
$PinSha  = "0c7be696371069e0baa2515ed7aa08d5a5473aa6"   # libwhisper 1.9.5 / ggml 0.26.0
$PinVer  = "1.9"                                          # major.minor the app guards
$repoUrl = "https://github.com/ggml-org/whisper.cpp"

if (-not $OutputDir) { $OutputDir = Join-Path $repo "native\win-x64" }

# ---- preflight: fail fast with actionable install instructions ---------------
$missing = @()
foreach ($tool in @("git", "cmake")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { $missing += $tool }
}
if (-not (Get-Command "g++" -ErrorAction SilentlyContinue)) { $missing += "g++ (MinGW-w64 x64)" }
# The "MinGW Makefiles" generator needs a make; MSYS2's gcc package provides it.
if (-not (Get-Command "mingw32-make" -ErrorAction SilentlyContinue) -and -not (Get-Command "make" -ErrorAction SilentlyContinue)) {
    $missing += "mingw32-make"
}
if ($missing.Count -gt 0) {
    throw ("Missing tool(s) on PATH: {0}.`nInstall them first, e.g.:`n  winget install --id Git.Git`n  winget install --id Kitware.CMake`n  winget install --id MSYS2.MSYS2    # then inside MSYS2: pacman -S mingw-w64-x86_64-gcc`nThen REOPEN this PowerShell session so the new tools are on PATH, and run this script again." -f ($missing -join ", "))
}

# ---- source: pinned checkout (fetched once, reused afterwards) ---------------
if (-not $SourceDir) { $SourceDir = Join-Path $repo "..\tools\whisper.cpp" }
$SourceDir = (Resolve-Path -ErrorAction SilentlyContinue $SourceDir)

if (-not $SourceDir) {
    Write-Host "==> Fetching whisper.cpp source (one time)" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path (Split-Path $SourceDir) | Out-Null
    git clone --quiet $repoUrl $SourceDir
    if ($LASTEXITCODE -ne 0) { throw "git clone failed (exit $LASTEXITCODE)." }
}

Write-Host "==> Checking out pinned commit $PinSha" -ForegroundColor Cyan
git -C $SourceDir checkout --quiet $PinSha
if ($LASTEXITCODE -ne 0) { throw "checkout of pinned SHA failed (exit $LASTEXITCODE)." }
git -C $SourceDir submodule update --init --recursive --quiet
$head = (git -C $SourceDir rev-parse HEAD).Trim()
if ($head -ne $PinSha) { throw "Pinned SHA mismatch: got $head, expected $PinSha." }

# ---- build: CMake Release, shared libs, portable CPU code -------------------
$buildDir = Join-Path $SourceDir "build"
Write-Host "==> Building the pinned runtime (libwhisper $PinVer, commit $PinSha)" -ForegroundColor Cyan
Write-Host "==> cmake configure (MinGW Makefiles, Release, shared libs)" -ForegroundColor Cyan
# _WIN32_WINNT=0x0601: ggml's Windows-11 thread-power-throttling block references
# THREAD_POWER_THROTTLING_STATE, which older MinGW-w64 SDK headers lack. Pinning
# the target to 0x0601 compiles the exact same pinned source everywhere
# (the block is a Windows-11 perf hint only; correctness is unaffected).
cmake -S $SourceDir -B $buildDir -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON -DGGML_NATIVE=OFF -DCMAKE_C_FLAGS="-D_WIN32_WINNT=0x0601" -DCMAKE_CXX_FLAGS="-D_WIN32_WINNT=0x0601"
if ($LASTEXITCODE -ne 0) {
    throw ("cmake configure failed (exit {0}). Need CMake + MinGW-w64 x64 g++ in PATH (e.g. MSYS2 mingw64).`nIf the error mentions a mismatched generator, a stale build directory from another toolchain exists - delete it and re-run: {1}" -f $LASTEXITCODE, $buildDir)
}

Write-Host "==> cmake build (this can take a few minutes)" -ForegroundColor Cyan
cmake --build $buildDir --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw "cmake build failed (exit $LASTEXITCODE)." }

# ---- stage the runtime next to the app at publish time -----------------------
$binDir = Join-Path $buildDir "bin"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# MinGW names the shared library libwhisper.dll; MSVC-style builds whisper.dll.
$libs = @()
$libs += Get-ChildItem $binDir -Include "whisper.dll","libwhisper.dll" -ErrorAction SilentlyContinue
$libs += Get-ChildItem $binDir -Filter "ggml*.dll" -ErrorAction SilentlyContinue
if ($libs.Count -eq 0) {
    throw "no whisper shared library (whisper.dll/libwhisper.dll) found in $binDir — check the cmake output above."
}
if (-not ($libs | Where-Object { $_.Name -like "ggml*.dll" })) {
    throw "the whisper library was built but no ggml*.dll siblings found in $binDir; the runtime would fail to load. Found: $($libs.Name -join ', ')"
}

foreach ($l in $libs) { Copy-Item $l.FullName $OutputDir -Force }

# ---- MinGW runtime DLLs (required next to the app on a stock Windows) ------
# The MinGW build links libgcc/libstdc++ dynamically; bundle the small runtime
# DLLs from the toolchain directory (next to g++, e.g. C:\msys64\mingw64\bin).
$gpp = (Get-Command g++ -ErrorAction SilentlyContinue).Source
$rtDir = if ($gpp) { Split-Path $gpp } else { "C:\msys64\mingw64\bin" }
foreach ($rt in @("libgcc_s_seh-1.dll", "libstdc++-6.dll", "libwinpthread-1.dll", "libgomp-1.dll")) {
    $src = Join-Path $rtDir $rt
    if (Test-Path $src) {
        Copy-Item $src $OutputDir -Force
    }
    else {
        Write-Host "NOTE: $rt not found in $rtDir — copy it into $OutputDir manually or the app will report a missing DLL." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host ("==> Staged {0} runtime DLLs in {1}:" -f $libs.Count, $OutputDir) -ForegroundColor Green
$libs | ForEach-Object { Write-Host ("    {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
Write-Host "==> Bundle it next to the app:  .\scripts\publish.ps1" -ForegroundColor Yellow
