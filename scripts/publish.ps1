# -----------------------------------------------------------------------------
# AusbildungsWeg Interview — reproducible Windows x64 Release publishing.
#
# Usage (from the repository root, in PowerShell):
#   .\scripts\publish.ps1                     # framework-dependent (default, small)
#   .\scripts\publish.ps1 -SelfContained      # self-contained (no .NET needed on target)
#   .\scripts\publish.ps1 -Zip                # also produce a distributable ZIP
#
# Output:  publish\AusbildungsWegInterview-windows-x64[...\]\
# Models (whisper ggml-*.bin, Ollama) are NOT included — see README "Models".
# No administrator rights are required; nothing is installed system-wide.
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$Zip
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
