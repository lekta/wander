<#
.SYNOPSIS
    Смена снимка по логу сеанса: таблица по папкам (PLAN AK, PERFORMANCE).

.DESCRIPTION
    Разбирает один лог Wander (%LOCALAPPDATA%\Wander\logs\session-*.log),
    снятый на релизной сборке с трассой действий («Отладка» -> «Подробно:
    клавиши, клики, выделение»): без неё нет строк `WS ListSelectionChanged`,
    и смены считать нечем. Ничего не запускает и не пишет.

    По каждой папке (`WS Navigated`): смен выделения одного файла; строк и
    вызовов `PERF preview.shown` (только смены >= 33 мс - PerfLog пишет
    заметное; смена без строки быстрее) и худшая; соседи, декодированные
    заранее (`bg.preview-ahead`), и полный кадр для лупы (`bg.preview-whole`).
    Внизу - итоги и память по строкам `SYS`: пик working set и private,
    максимум LOH, сборки gen2 к концу сеанса.

    Числа в логе - в локали процесса (`84,8 ms`), запятая понимается.

.EXAMPLE
    .\tools\preview-perf.ps1 $env:LOCALAPPDATA\Wander\logs\session-20260924-155105-10068.log
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Log
)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Ms([string] $text) {
    return [double]::Parse(($text -replace ',', '.'), $inv)
}

$folders = [ordered]@{}
function Row([string] $name) {
    if (-not $folders.Contains($name)) {
        $folders[$name] = [pscustomobject]@{
            Folder = $name; Changes = 0
            ShownLines = 0; ShownCalls = 0; Worst = 0.0
            Ahead = 0; AheadWorst = 0.0
            Whole = 0; WholeWorst = 0.0
        }
    }
    return $folders[$name]
}

function Perf([object] $row, [string] $countProperty, [string] $worstProperty, [int] $calls, [double] $worst) {
    $row.$countProperty += $calls
    if ($worst -gt $row.$worstProperty) {
        $row.$worstProperty = $worst
    }
}

$current = '(до первой папки)'
$sysLines = 0; $wsPeak = 0; $privatePeak = 0; $lohPeak = 0; $gen2 = 0
$perf = '([\d,\.]+) ms in (\d+) calls, worst ([\d,\.]+) ms'

foreach ($line in Get-Content -LiteralPath $Log -Encoding UTF8) {
    if ($line -match 'WS Navigated \{ Path = (.+?), Source = ') {
        $current = $Matches[1]
        $null = Row $current
    } elseif ($line -match 'WS ListSelectionChanged \(1, ') {
        (Row $current).Changes++
    } elseif ($line -match "PERF preview\.shown: $perf") {
        $row = Row $current
        $row.ShownLines++
        Perf $row 'ShownCalls' 'Worst' ([int]$Matches[2]) (Ms $Matches[3])
    } elseif ($line -match "PERF bg\.preview-ahead: $perf") {
        Perf (Row $current) 'Ahead' 'AheadWorst' ([int]$Matches[2]) (Ms $Matches[3])
    } elseif ($line -match "PERF bg\.preview-whole: $perf") {
        Perf (Row $current) 'Whole' 'WholeWorst' ([int]$Matches[2]) (Ms $Matches[3])
    } elseif ($line -match ' SYS ws=(\d+) private=(\d+) gen=\d+/\d+/(\d+)(?: alloc=\S+)? loh=(\d+)') {
        $sysLines++
        $wsPeak = [Math]::Max($wsPeak, [int]$Matches[1])
        $privatePeak = [Math]::Max($privatePeak, [int]$Matches[2])
        $gen2 = [int]$Matches[3]
        $lohPeak = [Math]::Max($lohPeak, [int]$Matches[4])
    }
}

$rows = @($folders.Values | Where-Object { $_.Changes -gt 0 -or $_.ShownCalls -gt 0 })
$rows | Format-Table -AutoSize -Wrap `
    @{ n = 'Папка'; e = { $_.Folder } },
    @{ n = 'Смен'; e = { $_.Changes } },
    @{ n = 'shown стр'; e = { $_.ShownLines } },
    @{ n = 'shown выз'; e = { $_.ShownCalls } },
    @{ n = 'Худший'; e = { [Math]::Round($_.Worst) } },
    @{ n = 'Соседей'; e = { $_.Ahead } },
    @{ n = 'Сосед худ'; e = { [Math]::Round($_.AheadWorst) } },
    @{ n = 'Кадров'; e = { $_.Whole } },
    @{ n = 'Кадр худ'; e = { [Math]::Round($_.WholeWorst) } }

$changes = ($rows | Measure-Object Changes -Sum).Sum
$shown = ($rows | Measure-Object ShownCalls -Sum).Sum
$worst = ($rows | Measure-Object Worst -Maximum).Maximum
"Итого: смен $changes, в preview.shown попало $shown, худшая $([Math]::Round($worst)) мс."
if ($sysLines -gt 0) {
    "SYS: строк $sysLines, пик ws $wsPeak МБ / private $privatePeak МБ, LOH до $lohPeak МБ, gen2 к концу $gen2."
}
