# Собирает портативный self-contained Wander.exe (то же, что делает CI).
# Запускается либо вручную, либо через run-configuration «Publish release exe»
# в Rider (правый верхний угол → выпадающий список конфигураций).
#
# -Install [-InstallDir C:\Programs\Wander]: положить собранный exe туда,
# откуда человек запускает Wander как обычную программу (конфигурация Rider
# «Publish and install»). Подменять exe под работающим экземпляром нельзя:
# однофайловая сборка дочитывает свои ещё не загруженные сборки из exe по
# пути, и после подмены первая же такая загрузка падает (закрытие окна —
# всегда). Поэтому после сборки запущенные из папки установки экземпляры
# закрываются штатно (как крестиком: состояние сохраняется), exe
# заменяется, и, если что-то было запущено, новый стартует сам. Не
# закрылся за 30 с (вопрос об идущих операциях, окна нет) — установка
# отменяется, файл не тронут. Хвосты .old прежних установок подметаются.
# Данные установленной копии — %LOCALAPPDATA%\Wander.
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
# When it was built: Rider's terminal keeps no times, and "did I publish
# after that fix" is otherwise unanswerable from the scrollback.
$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

Write-Host ""
Write-Host "OK  -> $exe  (${size} MB, $version)  $stamp" -ForegroundColor Green
Write-Host "SHA256: $hash"

$toRun = $exe
$restart = $false

if ($Install) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $installFull = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
    $target = Join-Path $installFull 'Wander.exe'

    # Closed before the swap, while their own exe is still in place - see
    # the header. A renamed .old one still reports the original path.
    $running = @(Get-Process -Name 'Wander' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and (Split-Path -Parent $_.Path) -eq $installFull })
    $stuck = @()
    foreach ($p in $running) {
        Write-Host "Closing Wander (PID $($p.Id))..." -ForegroundColor Cyan
        # No window to close: hung on its way out, nothing will answer.
        if ($p.MainWindowHandle -eq [IntPtr]::Zero -or -not $p.CloseMainWindow()) {
            $stuck += $p.Id
        }
    }
    foreach ($p in $running) {
        # 30 s: the close may ask about running operations, and stopping
        # them takes a moment after the answer.
        if ($stuck -notcontains $p.Id -and -not $p.WaitForExit(30000)) {
            $stuck += $p.Id
        }
    }
    if ($stuck.Count -gt 0) {
        $ids = $stuck -join ','
        Write-Host "NOT installed: Wander did not close (PID $ids), the installed exe is untouched." -ForegroundColor Red
        Write-Host "  Close it and run again, or stop it: Stop-Process -Id $ids" -ForegroundColor Red
        exit 1
    }
    $restart = $running.Count -gt 0

    # Leftovers of the installs that renamed the running exe aside.
    foreach ($stale in Get-ChildItem -Path $installFull -Filter 'Wander.exe.*.old' -File) {
        try {
            Remove-Item -Path $stale.FullName -Force -ErrorAction Stop
        } catch {
            Write-Host "  still in use, left alone: $($stale.Name)" -ForegroundColor DarkYellow
        }
    }

    Copy-Item -Path $exe -Destination $target -Force

    Write-Host "Installed -> $target  $stamp" -ForegroundColor Green
    $toRun = $target
}

if ($Run -or $restart) {
    Write-Host ""
    Write-Host "Starting $toRun ..." -ForegroundColor Cyan
    # Own window, own lifetime: the script is done, the app is not.
    Start-Process -FilePath $toRun
}
