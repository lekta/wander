# Собирает портативный self-contained Wander.exe (то же, что делает CI).
# Запускается либо вручную, либо через run-configuration «Publish release exe»
# в Rider (правый верхний угол → выпадающий список конфигураций).

$ErrorActionPreference = 'Stop'

# Корень репозитория — родитель папки tools, где лежит этот скрипт.
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$outDir = Join-Path $repoRoot 'publish'

Write-Host "Publishing Wander (Release, portable single-file)..." -ForegroundColor Cyan

dotnet publish src\Wander.App `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outDir

$exe = Join-Path $outDir 'Wander.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "OK  -> $exe  (${size} MB)" -ForegroundColor Green
Write-Host "SHA256: $hash"
