# Собирает портативный self-contained Wander.exe (то же, что делает CI).
# Запускается либо вручную, либо через run-configuration «Publish release exe»
# в Rider (правый верхний угол → выпадающий список конфигураций).
#
# -Install [-InstallDir C:\Programs\Wander]: положить собранный exe туда,
# откуда человек запускает Wander как обычную программу (конфигурация Rider
# «Publish and install»). Запущенный exe перезаписать нельзя, но можно
# переименовать: образ держится по хендлу, а не по имени. Поэтому старый
# Wander.exe уезжает в Wander.exe.<время>.old, новый встаёт на его место,
# работающий экземпляр доживает на старом файле, следующий запуск — уже
# новый; ярлык на панели задач держится за путь и подмену переживает.
# Старые .old подметаются при следующем -Install; занятый остаётся до
# следующего раза. Данные установленной копии — %LOCALAPPDATA%\Wander.
#
# -Run: сразу запустить собранный exe (с -Install — установленную копию).
# Это единственный способ пощупать приложение ровно таким, каким его
# получит пользователь: Debug-сборка из Rider стартует заметно медленнее,
# и мерить по ней бессмысленно. В Rider для этого есть конфигурация
# «Run release exe».

param(
    [switch]$Run,
    [switch]$Install,
    [string]$InstallDir = 'C:\Programs\Wander'
)

$ErrorActionPreference = 'Stop'

# Корень репозитория — родитель папки tools, где лежит этот скрипт.
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$outDir = Join-Path $repoRoot 'publish'

Write-Host "Publishing Wander (Release, portable single-file)..." -ForegroundColor Cyan

# Номер сборки (PLAN AH). Для -Install собираем как обычно, с номером: это
# рабочая копия человека, и по номеру видно, какая именно. Всё остальное —
# то, что уезжает пользователю: WanderRelease=true, четвёртое число 0,
# версия названа тремя числами, как в гите.
# @(...) around the if: an if used as a value unrolls a one-item array into a
# bare string, and PowerShell 5.1 splats a string char by char.
$release = @(if (-not $Install) { '-p:WanderRelease=true' })

dotnet publish src\Wander.App `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    @release `
    -o $outDir

# dotnet — нативный процесс, Stop его код возврата не видит. Без этой
# проверки упавшая сборка установила бы exe от прошлого раза.
if ($LASTEXITCODE -ne 0) {
    Write-Host "publish FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = Join-Path $outDir 'Wander.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$version = (Get-Item $exe).VersionInfo.ProductVersion

Write-Host ""
Write-Host "OK  -> $exe  (${size} MB, $version)" -ForegroundColor Green
Write-Host "SHA256: $hash"

$toRun = $exe

if ($Install) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $target = Join-Path $InstallDir 'Wander.exe'

    # Хвосты прошлых установок. Тот, на котором ещё работает старый
    # экземпляр, удалить нельзя — он дождётся следующего раза.
    foreach ($stale in Get-ChildItem -Path $InstallDir -Filter 'Wander.exe.*.old' -File) {
        try {
            Remove-Item -Path $stale.FullName -Force -ErrorAction Stop
        } catch {
            Write-Host "  still in use, left alone: $($stale.Name)" -ForegroundColor DarkYellow
        }
    }

    if (Test-Path $target) {
        $old = Join-Path $InstallDir ('Wander.exe.{0:yyyyMMdd-HHmmss}.old' -f (Get-Date))
        Move-Item -Path $target -Destination $old -Force
    }
    Copy-Item -Path $exe -Destination $target -Force

    Write-Host "Installed -> $target" -ForegroundColor Green
    $toRun = $target
}

if ($Run) {
    Write-Host ""
    Write-Host "Starting $toRun ..." -ForegroundColor Cyan
    # Own window, own lifetime: the script is done, the app is not.
    Start-Process -FilePath $toRun
}
