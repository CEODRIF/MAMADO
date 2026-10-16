# -----------------------------------------------------------------------------
# AusbildungsWeg Interview — reproducible Windows x64 Release publishing.
#
# Usage (from the repository root, in PowerShell):
#   .\scripts\publish.ps1                     # framework-dependent (default, small)
#   .\scripts\publish.ps1 -SelfContained      # self-contained (no .NET needed on target)
#   .\scripts\publish.ps1 -Zip                # also produce a distributable ZIP
#   .\scripts\publish.ps1 -NativeDir <path>   # where to find the built whisper runtime
#
# Output:  publish\AusbildungsWegInterview-windows-x64[...]\
# The pinned whisper.cpp native runtime (whisper.dll + ggml*.dll, built by
# scripts\build-whisper.ps1 into native\win-x64) IS bundled next to the app.
# Models (whisper ggml-*.bin, Ollama) are NOT included — see README "Models".
# No administrator rights are required; nothing is installed system-wide.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$Zip,
    [string]$NativeDir = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$app    = "src\AusbildungsWegInterview.App\AusbildungsWegInterview.App.csproj"
$rid    = "win-x64"
$mode   = if ($SelfContained) { "true" } else { "false" }
$outDir = Join-Path (Join-Path $repo "publish") "AusbildungsWegInterview-windows-x64"

Write-Host "==> dotnet publish (-c Release -r $rid --self-contained $mode)" -ForegroundColor Cyan
dotnet publish $app -c Release -r $rid --self-contained $mode -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$exe = Join-Path $outDir "AusbildungsWegInterview.exe"
if (-not (Test-Path $exe)) { throw "Expected executable not found: $exe" }
Write-Host "==> Published to $outDir" -ForegroundColor Green

# ---- native whisper runtime (the app needs it next to the executable) -------
if (-not $NativeDir) { $NativeDir = Join-Path $repo "native\win-x64" }
# The staged runtime directory holds the whisper+ggml DLLs plus the MinGW
# runtime DLLs (libgcc_s_seh-1.dll, libstdc++-6.dll, libwinpthread-1.dll) —
# copy everything that build-whisper.ps1 staged.
$nativeDlls = @(Get-ChildItem $NativeDir -File -ErrorAction SilentlyContinue)
if ($nativeDlls.Count -ge 1) {
    foreach ($d in $nativeDlls) { Copy-Item $d.FullName $outDir -Force }
    Write-Host ("==> Bundled {0} native runtime DLL(s) from {1}" -f $nativeDlls.Count, $NativeDir) -ForegroundColor Green

    # Verify the FULL dependency set — a partial copy still fails at load time
    # (libwhisper needs its ggml siblings, which need the MinGW runtime).
    $have = $nativeDlls.Name
    $partial = @()
    if (-not ($have | Where-Object { $_ -eq "whisper.dll" -or $_ -eq "libwhisper.dll" })) {
        $partial += "whisper.dll/libwhisper.dll"
    }
    foreach ($d in @("ggml.dll","ggml-base.dll","ggml-cpu.dll","libgcc_s_seh-1.dll","libstdc++-6.dll","libwinpthread-1.dll","libgomp-1.dll")) {
        if ($have -notcontains $d) { $partial += $d }
    }
    if ($partial.Count -gt 0) {
        Write-Host ("WARNING: staged runtime is INCOMPLETE - missing: {0}" -f ($partial -join ", ")) -ForegroundColor Red
        Write-Host "         Re-run scripts\build-whisper.ps1 (it stages the full set) and publish again." -ForegroundColor Red
    }
} else {
    Write-Host "WARNING: no native whisper runtime found in $NativeDir." -ForegroundColor Red
    Write-Host "         Run scripts\build-whisper.ps1 first, or pass -NativeDir." -ForegroundColor Red
    Write-Host "         Without it, transcription shows a clear, localized error in the app." -ForegroundColor Red
}

if ($Zip) {
    $zipPath = Join-Path $repo "publish\AusbildungsWegInterview-windows-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Write-Host "==> Creating $zipPath" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath
    Write-Host ("==> ZIP size: {0:N1} MB" -f ((Get-Item $zipPath).Length / 1MB)) -ForegroundColor Green
}

Write-Host ""
Write-Host "Launch:  double-click AusbildungsWegInterview.exe (or: & `"$exe`")" -ForegroundColor Yellow
if (-not $SelfContained) {
    Write-Host "NOTE: framework-dependent build — the target machine needs the" -ForegroundColor Yellow
    Write-Host ".NET 8 Desktop Runtime (x64): https://dotnet.microsoft.com/download" -ForegroundColor Yellow
}
Write-Host "Remove:  delete the publish folder (config lives in %APPDATA%\AusbildungsWegInterview)" -ForegroundColor Yellow
