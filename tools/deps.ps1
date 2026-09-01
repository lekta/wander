# Граф зависимостей «от -> к» — проекты и папки внутри проектов (шаг O7).
#
# Дешёвый свод using-ов, а не анализ компилятора: каждая строка
# `using Wander.X.Y;` (включая alias и static) считается ребром «папка
# файла -> папка namespace-а». Того, что внутри одной папки, рёбра не
# видят — там using не нужен; полностью квалифицированные обращения без
# using тоже не видны. Для ответа «кто от кого зависит и где циклы»
# этого достаточно, и результат повторяем.
#
# Запуск из корня репозитория:
#
#     .\tools\deps.ps1                отчёт на экран
#     .\tools\deps.ps1 -Out deps.txt  отчёт ещё и в файл
#     .\tools\deps.ps1 -UpdateDoc     перезаписать блок в docs/ARCHITECTURE.md
#                                     (между маркерами deps:generated)
#
# Что считается:
#   * рёбра проект -> проект;
#   * для каждого проекта: рёбра папка -> папка с числом файлов;
#   * уровни папок (0 — ни от кого в проекте не зависит; N — самый
#     длинный путь вниз из N шагов); папки в цикле уровня не имеют;
#   * циклы: рёбра, оставшиеся после отшелушивания листьев;
#   * файлы, чей namespace не совпадает с папкой на диске.
#
# Вывод по-английски намеренно — как в metrics.ps1: без BOM Windows
# PowerShell 5.1 читает файл как ANSI и на кириллице в строках падает.

[CmdletBinding()]
param(
    [string]$Out,
    [switch]$UpdateDoc
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Как в metrics.ps1: рабочее дерево, а не индекс, и без исчезнувших файлов.
$tracked = @(git ls-files --cached --others --exclude-standard) | Where-Object { Test-Path -LiteralPath $_ }
$csFiles = $tracked | Where-Object { $_ -like 'src/*.cs' -or $_ -like 'tests/*.cs' }

# Проект и папка файла — из пути на диске.
# src/Wander.App/Controllers/X.cs -> Wander.App : Controllers
# src/Wander.Core/ServiceLocator.cs -> Wander.Core : (root)
function Get-Location2([string]$path) {
    if ($path -notmatch '^(src|tests)/([^/]+)/(.+)$') {
        return $null
    }
    $project = $matches[2]
    $rest = $matches[3]
    $folder = if ($rest -match '^([^/]+)/') { $matches[1] } else { '(root)' }

    return [pscustomobject]@{ Project = $project; Folder = $folder }
}

# Namespace -> (проект, папка). Сегмент после имени проекта — папка, но
# только если такая папка в проекте существует: `using static
# Wander.Core.ServiceLocator` указывает на тип в корне, а не на папку.
$knownFolders = @{}
foreach ($f in $csFiles) {
    $loc = Get-Location2 $f
    if ($null -ne $loc -and $loc.Folder -ne '(root)') {
        $knownFolders[($loc.Project + '.' + $loc.Folder)] = $true
    }
}

function Resolve-Namespace([string]$ns) {
    $project = $null
    $tail = $null
    if ($ns -match '^Wander\.Platform\.Windows(\.(.+))?$') {
        $project = 'Wander.Platform.Windows'
        $tail = $matches[2]
    } elseif ($ns -match '^Wander\.(Core|App)(\.(.+))?$') {
        $project = 'Wander.' + $matches[1]
        $tail = $matches[3]
    } else {
        return $null
    }

    $folder = '(root)'
    if ($tail) {
        $first = ($tail -split '\.')[0]
        if ($knownFolders.ContainsKey($project + '.' + $first)) {
            $folder = $first
        }
    }

    return [pscustomobject]@{ Project = $project; Folder = $folder }
}

# --- Сбор рёбер ------------------------------------------------------

# Ключ "проект|из|проект|в" -> множество файлов, давших ребро.
$edges = @{}
$mismatches = New-Object System.Collections.Generic.List[string]

foreach ($f in $csFiles) {
    $from = Get-Location2 $f
    if ($null -eq $from) {
        continue
    }

    $lines = @(Get-Content -LiteralPath $f -Encoding UTF8)
    $declared = $null
    foreach ($line in $lines) {
        # using Wander.X; / using static Wander.X.Y; / using A = Wander.X.Y;
        $ns = $null
        if ($line -match '^\s*using\s+static\s+(Wander[\w.]*)\s*;') {
            $ns = $matches[1]
        } elseif ($line -match '^\s*using\s+\w+\s*=\s*(Wander[\w.]*)') {
            $ns = $matches[1]
        } elseif ($line -match '^\s*using\s+(Wander[\w.]*)\s*;') {
            $ns = $matches[1]
        } elseif ($null -eq $declared -and $line -match '^\s*namespace\s+([\w.]+)') {
            $declared = $matches[1]
            continue
        } else {
            continue
        }

        $to = Resolve-Namespace $ns
        if ($null -eq $to) {
            continue
        }
        if ($to.Project -eq $from.Project -and $to.Folder -eq $from.Folder) {
            continue
        }

        $key = $from.Project + '|' + $from.Folder + '|' + $to.Project + '|' + $to.Folder
        if (-not $edges.ContainsKey($key)) {
            $edges[$key] = New-Object System.Collections.Generic.HashSet[string]
        }
        [void]$edges[$key].Add($f)
    }

    if ($declared) {
        $expected = if ($from.Folder -eq '(root)') { $from.Project } else { $from.Project + '.' + $from.Folder }
        # Тестовый проект живёт в namespace Wander.Core.Tests.* — это норма.
        if ($from.Project -eq 'Wander.Core.Tests') {
            $expected = 'Wander.Core.Tests'
            if ($declared -ne $expected -and $declared -notlike ($expected + '.*')) {
                $mismatches.Add(("{0}  declares {1}" -f $f, $declared))
            }
        } elseif ($declared -ne $expected) {
            $mismatches.Add(("{0}  declares {1}" -f $f, $declared))
        }
    }
}

# --- Отчёт -----------------------------------------------------------

$report = New-Object System.Collections.Generic.List[string]
function Add-Line([string]$text) {
    $script:report.Add($text)
}

Add-Line "=== Wander dependency graph (using sweep) ==="
Add-Line ("date   : " + (Get-Date -Format 'yyyy-MM-dd'))
Add-Line ("commit : " + (git rev-parse --short HEAD))
Add-Line ""

# Проект -> проект.
Add-Line "-- projects --"
$projEdges = @{}
foreach ($key in $edges.Keys) {
    $parts = $key -split '\|'
    if ($parts[0] -eq $parts[2]) {
        continue
    }
    $pk = $parts[0] + ' -> ' + $parts[2]
    if (-not $projEdges.ContainsKey($pk)) {
        $projEdges[$pk] = New-Object System.Collections.Generic.HashSet[string]
    }
    $projEdges[$pk].UnionWith($edges[$key])
}
foreach ($pk in ($projEdges.Keys | Sort-Object)) {
    Add-Line ("{0}   ({1} files)" -f $pk, $projEdges[$pk].Count)
}
Add-Line ""

# Папки внутри каждого проекта.
foreach ($project in @('Wander.Core', 'Wander.Platform.Windows', 'Wander.App')) {
    Add-Line ("-- {0}: folder -> folder --" -f $project)

    # Рёбра внутри проекта.
    $inner = @{}      # from -> set of to
    $lines = @()
    foreach ($key in ($edges.Keys | Sort-Object)) {
        $parts = $key -split '\|'
        if ($parts[0] -ne $project -or $parts[2] -ne $project) {
            continue
        }
        $lines += ("{0,-14} -> {1,-14} ({2} files)" -f $parts[1], $parts[3], $edges[$key].Count)
        if (-not $inner.ContainsKey($parts[1])) {
            $inner[$parts[1]] = New-Object System.Collections.Generic.HashSet[string]
        }
        [void]$inner[$parts[1]].Add($parts[3])
    }
    foreach ($l in $lines) {
        Add-Line ("  " + $l)
    }

    # Все папки проекта — и те, что ни от кого не зависят.
    $nodes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($f in $csFiles) {
        $loc = Get-Location2 $f
        if ($null -ne $loc -and $loc.Project -eq $project) {
            [void]$nodes.Add($loc.Folder)
        }
    }
    foreach ($from in $inner.Keys) {
        [void]$nodes.Add($from)
        foreach ($to in $inner[$from]) {
            [void]$nodes.Add($to)
        }
    }

    # В цикле — узел, из которого есть путь в самого себя. Считается по
    # транзитивному замыканию: граф маленький, квадратичность не видна.
    $reach = @{}
    foreach ($from in $inner.Keys) {
        $reach[$from] = New-Object System.Collections.Generic.HashSet[string]
        $reach[$from].UnionWith($inner[$from])
    }
    $grew = $true
    while ($grew) {
        $grew = $false
        foreach ($from in @($reach.Keys)) {
            foreach ($mid in @($reach[$from])) {
                if ($reach.ContainsKey($mid)) {
                    foreach ($to in $reach[$mid]) {
                        if ($reach[$from].Add($to)) {
                            $grew = $true
                        }
                    }
                }
            }
        }
    }
    $cyclic = New-Object System.Collections.Generic.HashSet[string]
    foreach ($node in $nodes) {
        if ($reach.ContainsKey($node) -and $reach[$node].Contains($node)) {
            [void]$cyclic.Add($node)
        }
    }

    # Конденсация: взаимно достижимые узлы склеиваются в один узел-клубок,
    # получившийся граф ацикличен, и уровни считаются по нему. Клубок
    # получает уровень как целое и печатается как [A+B].
    $group = @{}
    foreach ($node in $nodes) {
        if (-not $cyclic.Contains($node)) {
            $group[$node] = $node
            continue
        }
        $members = @($cyclic | Where-Object { $_ -eq $node -or ($reach[$node].Contains($_) -and $reach[$_].Contains($node)) } | Sort-Object)
        $group[$node] = '[' + ($members -join '+') + ']'
    }

    $dagNodes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($node in $nodes) {
        [void]$dagNodes.Add($group[$node])
    }
    $dagEdges = @{}
    foreach ($from in $inner.Keys) {
        foreach ($to in $inner[$from]) {
            $gf = $group[$from]
            $gt = $group[$to]
            if ($gf -eq $gt) {
                continue
            }
            if (-not $dagEdges.ContainsKey($gf)) {
                $dagEdges[$gf] = New-Object System.Collections.Generic.HashSet[string]
            }
            [void]$dagEdges[$gf].Add($gt)
        }
    }

    # Уровни: 0 — ни от кого в проекте не зависит, дальше 1 + максимум.
    $level = @{}
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($node in $dagNodes) {
            if ($level.ContainsKey($node)) {
                continue
            }
            $deps = @()
            if ($dagEdges.ContainsKey($node)) {
                $deps = @($dagEdges[$node])
            }
            $unresolved = @($deps | Where-Object { -not $level.ContainsKey($_) })
            if ($unresolved.Count -eq 0) {
                $max = -1
                foreach ($d in $deps) {
                    if ($level[$d] -gt $max) {
                        $max = $level[$d]
                    }
                }
                $level[$node] = $max + 1
                $changed = $true
            }
        }
    }

    Add-Line ""
    Add-Line ("-- {0}: levels --" -f $project)
    foreach ($lv in ($level.Values | Sort-Object -Unique)) {
        $names = @($level.Keys | Where-Object { $level[$_] -eq $lv } | Sort-Object)
        Add-Line ("  {0}: {1}" -f $lv, ($names -join ', '))
    }
    if ($cyclic.Count -gt 0) {
        Add-Line "  cycle edges:"
        foreach ($from in ($cyclic | Sort-Object)) {
            if ($inner.ContainsKey($from)) {
                foreach ($to in ($inner[$from] | Sort-Object)) {
                    if ($cyclic.Contains($to) -and $group[$from] -eq $group[$to]) {
                        Add-Line ("    {0} -> {1}" -f $from, $to)
                    }
                }
            }
        }
    }
    Add-Line ""
}

Add-Line "-- namespace <> folder mismatches --"
if ($mismatches.Count -eq 0) {
    Add-Line "  (none)"
} else {
    foreach ($m in ($mismatches | Sort-Object)) {
        Add-Line ("  " + $m)
    }
}

$text = $report -join [Environment]::NewLine
Write-Output $text

if ($Out) {
    Set-Content -LiteralPath $Out -Value $text -Encoding utf8
    Write-Output ""
    Write-Output ("written to " + $Out)
}

if ($UpdateDoc) {
    $doc = 'docs/ARCHITECTURE.md'
    $begin = '<!-- deps:generated:begin -->'
    $end = '<!-- deps:generated:end -->'
    $content = [System.IO.File]::ReadAllText((Join-Path $repoRoot $doc))
    $iBegin = $content.IndexOf($begin)
    $iEnd = $content.IndexOf($end)
    if ($iBegin -lt 0 -or $iEnd -lt 0 -or $iEnd -lt $iBegin) {
        throw "markers $begin / $end not found in $doc"
    }
    $nl = "`r`n"
    if ($content -notmatch "`r`n") {
        $nl = "`n"
    }
    $block = $begin + $nl + '```' + $nl + $text + $nl + '```' + $nl
    $updated = $content.Substring(0, $iBegin) + $block + $content.Substring($iEnd)
    [System.IO.File]::WriteAllText((Join-Path $repoRoot $doc), $updated, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ""
    Write-Output ("updated " + $doc)
}
