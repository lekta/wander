# Собирает замеры одной версии в один файл: docs/measurements/<версия>.md.
#
# Запускается на этапе «подготовка к релизу» (WORKFLOW.md), после
# publish.ps1 -Run и после полного прогона харнесса. Смысл в том, чтобы у
# каждой выпущенной версии остался снимок: сколько весит, как быстро
# стартует, что было в логах живого прогона, чем закончился набор сценариев.
# Через три версии «кажется, стало медленнее» проверяется за минуту, а не
# спором по памяти.
#
# Сам скрипт ничего не измеряет: он собирает уже намеренное — опубликованный
# exe, логи релизного прогона, артефакты харнесса. Чего нет, помечает n/a,
# и это видно в файле, а не заметается.
#
#     .\tools\release-metrics.ps1 -Version 0.3.1
#     .\tools\release-metrics.ps1 -Version 0.3.1 -Hours 6 -Out C:/tmp/probe.md
#
# Вывод по-английски намеренно — как в publish.ps1, size-report.ps1 и
# metrics.ps1: без BOM Windows PowerShell 5.1 читает файл как ANSI и на
# кириллице в строковых литералах выдаёт кашу.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Out,
    [string]$Exe,
    [string]$Logs,
    [string]$Artifacts,
    [int]$Hours = 24
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $Exe)       { $Exe = Join-Path $repoRoot 'publish/Wander.exe' }
if (-not $Logs)      { $Logs = Join-Path $env:LOCALAPPDATA 'Wander/logs' }
if (-not $Artifacts) { $Artifacts = Join-Path $repoRoot 'artifacts' }
if (-not $Out)       { $Out = Join-Path $repoRoot ('docs/measurements/' + $Version + '.md') }

$since = (Get-Date).AddHours(-$Hours)
$lines = New-Object System.Collections.Generic.List[string]

function Add-Line([string]$text) {
    $lines.Add($text) | Out-Null
}

# min / median / max одной строкой; пусто - "none", чтобы отличать "не было"
# от "было и быстро".
function Format-Stat($values) {
    if ($values.Count -eq 0) { return 'none' }
    $sorted = $values | Sort-Object
    $median = $sorted[[int]($sorted.Count / 2)]
    return ('' + $sorted[0] + ' / ' + $median + ' / ' + $sorted[-1] + ' ms over ' + $values.Count)
}

Write-Host "Collecting measurements for $Version ..." -ForegroundColor Cyan

# --- Окружение -------------------------------------------------------------

$os = Get-CimInstance Win32_OperatingSystem
$cpu = @(Get-CimInstance Win32_Processor)[0]
$ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 0)
$commit = (git rev-parse --short HEAD 2>$null)
if (-not $commit) { $commit = 'n/a' }
$sdk = (dotnet --version 2>$null)
if (-not $sdk) { $sdk = 'n/a' }

Add-Line "# Wander $Version - measurements"
Add-Line ""
Add-Line ("Collected " + (Get-Date -Format 'yyyy-MM-dd HH:mm') + " by tools/release-metrics.ps1.")
Add-Line "Release build only: Debug numbers are not comparable and do not belong in this file."
Add-Line ""
Add-Line "## Environment"
Add-Line ""
Add-Line "| | |"
Add-Line "|---|---|"
Add-Line ("| OS | " + $os.Caption + " build " + $os.BuildNumber + " |")
Add-Line ("| CPU | " + $cpu.Name.Trim() + " |")
Add-Line ("| RAM | " + $ramGb + " GB |")
Add-Line ("| .NET SDK | " + $sdk + " |")
Add-Line ("| commit | " + $commit + " |")
Add-Line ""

# --- Поставка --------------------------------------------------------------

Add-Line "## Package"
Add-Line ""
if (Test-Path $Exe) {
    $item = Get-Item $Exe
    $mib = [math]::Round($item.Length / 1MB, 1)
    $sha = (Get-FileHash $Exe -Algorithm SHA256).Hash
    Add-Line "| | |"
    Add-Line "|---|---|"
    Add-Line ("| exe | " + $item.FullName + " |")
    Add-Line ("| size | " + $mib + " MiB (" + $item.Length + " bytes) |")
    Add-Line ("| built | " + $item.LastWriteTime.ToString('yyyy-MM-dd HH:mm') + " |")
    Add-Line ("| SHA256 | " + $sha + " |")
    Add-Line ""
    Add-Line "Category breakdown: run tools/size-report.ps1 and paste its table here whenever the"
    Add-Line "size moved by more than a megabyte."
} else {
    Add-Line "n/a - no published exe at $Exe. Run tools/publish.ps1 first."
}
Add-Line ""

# --- Логи релизного прогона ------------------------------------------------
#
# Единственный источник про поведение на живых папках. Берутся только свежие
# сессии: логи копятся, и прошлая версия в этот файл попасть не должна.

Add-Line "## Release run (session logs, last $Hours h)"
Add-Line ""
$sessions = @()
if (Test-Path $Logs) {
    $sessions = @(Get-ChildItem $Logs -Filter '*.log' | Where-Object { $_.LastWriteTime -ge $since })
}
if ($sessions.Count -eq 0) {
    Add-Line "n/a - no session logs newer than $Hours h in $Logs."
    Add-Line "Run tools/publish.ps1 -Run, use the app for ten minutes, then run this again."
} else {
    $text = $sessions | Get-Content
    $frames = @($text | Select-String -Pattern 'first frame (\d+) ms' | ForEach-Object { [int]$_.Matches[0].Groups[1].Value })
    $stalls = @($text | Select-String -Pattern 'ui\.stall: (\d+) ms' | ForEach-Object { [int]$_.Matches[0].Groups[1].Value })
    $listed = @($text | Select-String -Pattern 'Folder listed in (\d+) ms' | ForEach-Object { [int]$_.Matches[0].Groups[1].Value })
    $warns = @($text | Select-String -Pattern 'WARN ')
    $errors = @($text | Select-String -Pattern 'ERROR ')
    $oldest = ($sessions | Sort-Object LastWriteTime | Select-Object -First 1).LastWriteTime

    Add-Line ("Sessions: " + $sessions.Count + ", oldest " + $oldest.ToString('yyyy-MM-dd HH:mm') + ".")
    Add-Line ""
    Add-Line "| what | min / median / max |"
    Add-Line "|---|---|"
    Add-Line ("| first frame | " + (Format-Stat $frames) + " |")
    Add-Line ("| ui.stall | " + (Format-Stat $stalls) + " |")
    Add-Line ("| folder listed (logged over 300 ms) | " + (Format-Stat $listed) + " |")
    Add-Line ("| WARN / ERROR lines | " + $warns.Count + " / " + $errors.Count + " |")

    if ($errors.Count -gt 0) {
        Add-Line ""
        Add-Line "Errors, most frequent first:"
        Add-Line ""
        $errors |
            ForEach-Object { $_.Line -replace '^\S+\s+\S+\s+', '' } |
            Group-Object |
            Sort-Object Count -Descending |
            Select-Object -First 5 |
            ForEach-Object { Add-Line ("- " + $_.Count + "x " + $_.Name) }
    }
}
Add-Line ""

# --- Харнесс ---------------------------------------------------------------
#
# По последнему прогону каждого сценария: результат из report.md, память и GC
# из metrics.json. Прогоны старше окна не берутся - это цифры прошлой версии.

Add-Line "## Harness (latest run of each scenario, last $Hours h)"
Add-Line ""
$runs = @()
if (Test-Path $Artifacts) {
    $runs = @(Get-ChildItem $Artifacts -Directory | Where-Object { $_.LastWriteTime -ge $since })
}
if ($runs.Count -eq 0) {
    Add-Line "n/a - no harness runs newer than $Hours h in $Artifacts."
} else {
    Add-Line "| scenario | result | WS peak | gen2 | handles | LOH peak |"
    Add-Line "|---|---|---|---|---|---|"
    $byScenario = $runs | Group-Object { $_.Name -replace '-\d{8}-\d{6}$', '' }
    foreach ($group in ($byScenario | Sort-Object Name)) {
        $run = $group.Group | Sort-Object LastWriteTime | Select-Object -Last 1
        $result = 'n/a'
        $report = Join-Path $run.FullName 'report.md'
        if (Test-Path $report) {
            $line = Get-Content $report | Select-String -Pattern 'result: ' | Select-Object -First 1
            if ($line) { $result = ($line.Line -replace '.*result: ', '') -replace '\*', '' }
        }
        $ws = 'n/a'
        $gen2 = 'n/a'
        $handles = 'n/a'
        $loh = 'n/a'
        $metrics = Join-Path $run.FullName 'metrics.json'
        if (Test-Path $metrics) {
            # foreach, а не @(... | ConvertFrom-Json): Windows PowerShell 5.1
            # отдаёт JSON-массив одним объектом и не разворачивает его в
            # конвейере, так что @() дало бы массив из одного массива.
            $samples = @()
            foreach ($sample in (Get-Content $metrics -Raw | ConvertFrom-Json)) {
                $samples += $sample
            }
            if ($samples.Count -gt 0) {
                $ws = '' + [math]::Round(($samples | Measure-Object WorkingSet -Maximum).Maximum / 1MB, 0) + ' MB'
                $loh = '' + [math]::Round(($samples | Measure-Object LohBytes -Maximum).Maximum / 1MB, 0) + ' MB'
                $gen2 = '' + ($samples[-1].Gen2 - $samples[0].Gen2)
                $handles = '' + ($samples | Measure-Object Handles -Minimum).Minimum + '..' + ($samples | Measure-Object Handles -Maximum).Maximum
            }
        }
        Add-Line ("| " + $group.Name + " | " + $result + " | " + $ws + " | " + $gen2 + " | " + $handles + " | " + $loh + " |")
    }
    Add-Line ""
    Add-Line "These are a baseline of the harness rather than of the app: the scenario driver runs"
    Add-Line "in the same process, in Debug. Compare them with the previous version, not with a"
    Add-Line "user's machine."
}
Add-Line ""

# --- Место под выводы ------------------------------------------------------

Add-Line "## Verdict"
Add-Line ""
Add-Line "Written by hand, and it is the point of the file: what moved against the previous"
Add-Line "version, what was decided about it, what stays a known cost. A number with no verdict"
Add-Line "is noise a year later."
Add-Line ""
Add-Line "- weight:"
Add-Line "- startup:"
Add-Line "- responsiveness:"
Add-Line "- memory and GC:"
Add-Line "- regressions, and where they went (blocker / PLAN / TECHDEBT):"
Add-Line ""

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($Out, ($lines -join [Environment]::NewLine), $encoding)

Write-Host "OK  -> $Out" -ForegroundColor Green
Write-Host "Fill in the Verdict section before committing."
