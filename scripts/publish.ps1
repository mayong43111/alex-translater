# Translater - Build & Publish Script
# Produces a self-contained x64 release in publish/

param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$AppProject = Join-Path $Root "src\Translater.App\Translater.App.csproj"
$OutputDir = Join-Path $Root "publish"

Write-Host "=== Building Translater (Release, win-x64) ===" -ForegroundColor Cyan

dotnet publish $AppProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $OutputDir `
    /p:PublishReadyToRun=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

$exe = Join-Path $OutputDir "Translater.App.exe"
$size = [math]::Round((Get-ChildItem $OutputDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host "Output: $OutputDir"
Write-Host "Size:   ${size} MB"
Write-Host "EXE:    $exe"

if ($Run) {
    Write-Host ""
    Write-Host "Launching..." -ForegroundColor Yellow
    Start-Process $exe
}
