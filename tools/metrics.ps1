# Метрики структуры кода — снимок для технических чисток.
#
# Смысл скрипта в том, чтобы снимки «до» и «после» были ОДНИМ И ТЕМ ЖЕ
# измерением. Посчитанное глазами через месяц — это уже другое измерение,
# и сравнивать его не с чем.
#
# Запуск из корня репозитория:
#
#     .\tools\metrics.ps1                 отчёт на экран
#     .\tools\metrics.ps1 -Top 40         длиннее списки
#     .\tools\metrics.ps1 -Out before.txt отчёт ещё и в файл
#
# Что считается:
#   * строки по проектам и самые большие файлы;
#   * публичная поверхность Wander.Core (типы и члены);
#   * места ServiceLocator.IsRegistered;
#   * самые длинные методы и самая глубокая вложенность в них;
#   * число using на файл.
#
# Разбор — грубый посимвольный сканер, а не парсер C#: строки, символьные
# литералы и комментарии вырезаются, дальше считаются фигурные скобки.
# Абсолютная точность здесь не нужна и недостижима; нужна повторяемость —
# один и тот же файл всегда даёт одно и то же число.
#
# Вывод по-английски намеренно — как в publish.ps1 и size-report.ps1:
# без BOM Windows PowerShell 5.1 читает файл как ANSI и на кириллице
# в строках падает.

[CmdletBinding()]
param(
    [int]$Top = 20,
    [string]$Out
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Ключевые слова, за которыми идёт скобка, но метода за ней нет.
$controlKeywords = @(
    'if', 'else', 'for', 'foreach', 'while', 'do', 'switch', 'case', 'catch',
    'try', 'finally', 'lock', 'using', 'fixed', 'return', 'throw', 'yield',
    'get', 'set', 'add', 'remove', 'when', 'select', 'where', 'new'
)

# Слова, которые стоят перед именем метода и именем не являются.
$modifierKeywords = @(
    'public', 'private', 'protected', 'internal', 'static', 'async',
    'override', 'virtual', 'sealed', 'partial', 'extern', 'unsafe', 'new',
    'readonly', 'ref', 'out', 'in', 'const', 'abstract', 'implicit',
    'explicit', 'operator'
)


# Убирает из строки то, что не должно влиять на счёт скобок: строковые и
# символьные литералы, построчные комментарии. Блочные комментарии
# обрабатывает вызывающий, потому что они переживают конец строки.
function Remove-Noise([string]$line) {
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $line.Length) {
        $c = $line[$i]

        if ($c -eq '/' -and $i + 1 -lt $line.Length -and $line[$i + 1] -eq '/') {
            break
        }

        if ($c -eq '"') {
            # Verbatim-строка @"..." внутри одной строки: удвоенная кавычка
            # экранирует саму себя, обратный слэш ничего не значит.
            $verbatim = ($i -gt 0 -and $line[$i - 1] -eq '@')
            $i++
            while ($i -lt $line.Length) {
                if ($verbatim) {
                    if ($line[$i] -eq '"') {
                        if ($i + 1 -lt $line.Length -and $line[$i + 1] -eq '"') {
                            $i += 2
                            continue
                        }
                        break
                    }
                } else {
                    if ($line[$i] -eq '\') {
                        $i += 2
                        continue
                    }
                    if ($line[$i] -eq '"') {
                        break
                    }
                }
                $i++
            }
            $i++
            [void]$sb.Append('""')
            continue
        }

        if ($c -eq "'") {
            $i++
            while ($i -lt $line.Length) {
                if ($line[$i] -eq '\') {
                    $i += 2
                    continue
                }
                if ($line[$i] -eq "'") {
                    break
                }
                $i++
            }
            $i++
            [void]$sb.Append("''")
            continue
        }

        [void]$sb.Append($c)
        $i++
    }

    return $sb.ToString()
}


# Похожа ли строка на начало метода или конструктора. Свойства и события
# сюда не попадают: у них нет круглых скобок перед телом.
function Test-MethodStart([string]$code) {
    $t = $code.Trim()
    if ($t -notmatch '\(') {
        return $false
    }
    if ($t -match '^\s*(\[|//|\*|#)') {
        return $false
    }
    # Первое слово строки. Если это управляющее ключевое слово — не метод.
    if ($t -match '^([A-Za-z_][A-Za-z0-9_]*)') {
        if ($controlKeywords -contains $matches[1]) {
            return $false
        }
    } else {
        return $false
    }
    # Вызов метода как выражение (заканчивается на ; или на запятую) —
    # не объявление. Объявление либо открывает тело, либо голое до {.
    if ($t -match ';\s*$') {
        return $false
    }
    if ($t -match '=>') {
        return $false
    }
    if ($t -match '^\s*(class|struct|interface|enum|record|namespace)\b') {
        return $false
    }

    return $true
}


# Имя метода из строки объявления — для отчёта, не для компилятора.
#
# Берётся первый идентификатор перед скобкой, который не модификатор.
# Проверка нужна из-за кортежного возврата: в
# `private static (int Count, long Size) CountAndSum(...)` первая скобка
# идёт сразу за `static`, и наивный разбор называет метод «static».
function Get-MethodName([string]$code) {
    foreach ($m in [regex]::Matches($code, '([A-Za-z_][A-Za-z0-9_]*)\s*(<[^>()]*>)?\s*\(')) {
        $name = $m.Groups[1].Value
        if ($modifierKeywords -notcontains $name) {
            return $name
        }
    }

    return '?'
}


# Разбирает один файл: длина методов, вложенность, using-и, публичные члены.
function Measure-File([string]$path) {
    $lines = @(Get-Content -LiteralPath $path -Encoding UTF8)

    $usings = 0
    $publicTypes = 0
    $publicMembers = 0
    $methods = @()

    $depth = 0
    $inBlockComment = $false

    $methodOpen = $false          # тело метода уже открылось
    $methodStartLine = 0
    $methodName = ''
    $methodBaseDepth = 0
    $methodMaxDepth = 0
    $pendingMethod = $false       # объявление увидено, тело ещё не открылось

    for ($n = 0; $n -lt $lines.Count; $n++) {
        $raw = $lines[$n]

        if ($raw -match '^\s*using\s+[A-Za-z_]') {
            $usings++
        }

        $code = $raw
        if ($inBlockComment) {
            $end = $code.IndexOf('*/')
            if ($end -lt 0) {
                continue
            }
            $code = $code.Substring($end + 2)
            $inBlockComment = $false
        }

        $start = $code.IndexOf('/*')
        while ($start -ge 0) {
            $end = $code.IndexOf('*/', $start + 2)
            if ($end -lt 0) {
                $code = $code.Substring(0, $start)
                $inBlockComment = $true
                break
            }
            $code = $code.Substring(0, $start) + ' ' + $code.Substring($end + 2)
            $start = $code.IndexOf('/*')
        }

        $code = Remove-Noise $code
        if ($code.Trim().Length -eq 0) {
            continue
        }

        if ($code -match '^\s*public\s+(sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|ref\s+)*(class|struct|interface|enum|record)\b') {
            $publicTypes++
        } elseif ($depth -ge 1 -and $code -match '^\s*public\b') {
            $publicMembers++
        }

        # Метод объявлен на этой строке — ждём открывающую скобку тела.
        # Она может стоять здесь же (1TBS) или на следующей строке.
        if (-not $methodOpen -and -not $pendingMethod -and $depth -ge 1 -and (Test-MethodStart $code)) {
            $pendingMethod = $true
            $methodStartLine = $n + 1
            $methodName = Get-MethodName $code
            $methodBaseDepth = $depth
        }

        $opens = ([regex]::Matches($code, '\{')).Count
        $closes = ([regex]::Matches($code, '\}')).Count

        # Скобки этой строки применяются по одной: закрытие тела метода
        # надо поймать ровно в тот момент, когда глубина вернулась к базовой.
        foreach ($ch in $code.ToCharArray()) {
            if ($ch -eq '{') {
                $depth++
                if ($pendingMethod) {
                    $pendingMethod = $false
                    $methodOpen = $true
                    $methodMaxDepth = 0
                }
                if ($methodOpen -and ($depth - $methodBaseDepth - 1) -gt $methodMaxDepth) {
                    $methodMaxDepth = $depth - $methodBaseDepth - 1
                }
            } elseif ($ch -eq '}') {
                $depth--
                if ($methodOpen -and $depth -le $methodBaseDepth) {
                    $methods += [pscustomobject]@{
                        File    = $path
                        Line    = $methodStartLine
                        Name    = $methodName
                        Length  = ($n + 1) - $methodStartLine + 1
                        Nesting = $methodMaxDepth
                    }
                    $methodOpen = $false
                }
            }
        }

        # Объявление без тела (интерфейс, abstract, partial) — снимаем ожидание.
        if ($pendingMethod -and $opens -eq 0 -and $code -match ';\s*$') {
            $pendingMethod = $false
        }
    }

    return [pscustomobject]@{
        Path          = $path
        Lines         = $lines.Count
        Usings        = $usings
        PublicTypes   = $publicTypes
        PublicMembers = $publicMembers
        Methods       = $methods
    }
}


function Get-Project([string]$path) {
    if ($path -match '^(src|tests)/([^/]+)/') {
        return $matches[2]
    }

    return '(root)'
}


# --others --exclude-standard добавляет ещё не закоммиченные файлы: снимок
# снимается по рабочему дереву, а не по индексу, иначе новый класс попадёт
# в метрику только после коммита — то есть уже после того, как по ней
# принимали решение.
$tracked = @(git ls-files --cached --others --exclude-standard)
$csFiles = $tracked | Where-Object { $_ -like '*.cs' -and $_ -ne 'src/Wander.App/AssemblyInfo.cs' }
$xamlFiles = $tracked | Where-Object { $_ -like '*.xaml' }

$measured = @()
foreach ($f in $csFiles) {
    $measured += Measure-File $f
}

$allMethods = @()
foreach ($m in $measured) {
    $allMethods += $m.Methods
}

$coreFiles = $measured | Where-Object { $_.Path -like 'src/Wander.Core/*' }
$srcMeasured = $measured | Where-Object { $_.Path -like 'src/*' }

$isRegisteredAll = @(git grep -o -n 'ServiceLocator\.IsRegistered' -- '*.cs').Count
$isRegisteredSrc = @(git grep -o -n 'ServiceLocator\.IsRegistered' -- 'src/*.cs').Count

$report = New-Object System.Collections.Generic.List[string]
function Add-Line([string]$text) {
    $script:report.Add($text)
}

$commit = (git rev-parse --short HEAD)
Add-Line "=== Wander code metrics ==="
Add-Line ("date   : " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
Add-Line ("commit : " + $commit)
Add-Line ""

Add-Line "-- lines by project --"
$byProject = $measured | Group-Object { Get-Project $_.Path } | Sort-Object Name
foreach ($g in $byProject) {
    $sum = ($g.Group | Measure-Object Lines -Sum).Sum
    Add-Line ("{0,-26} {1,7} lines  in {2,3} .cs files" -f $g.Name, $sum, $g.Count)
}
$xamlLines = 0
foreach ($x in $xamlFiles) {
    $xamlLines += @(Get-Content -LiteralPath $x -Encoding UTF8).Count
}
Add-Line ("{0,-26} {1,7} lines  in {2,3} files" -f 'XAML (all projects)', $xamlLines, $xamlFiles.Count)
Add-Line ""

Add-Line "-- largest .cs files --"
foreach ($m in ($measured | Sort-Object Lines -Descending | Select-Object -First $Top)) {
    Add-Line ("{0,6}  {1}" -f $m.Lines, $m.Path)
}
Add-Line ""

Add-Line "-- files over 500 lines --"
$over500 = $srcMeasured | Where-Object { $_.Lines -gt 500 }
Add-Line ("count: {0} in src (the split threshold from PLAN.md)" -f $over500.Count)
Add-Line ""

Add-Line "-- Wander.Core public surface --"
$coreTypes = ($coreFiles | Measure-Object PublicTypes -Sum).Sum
$coreMembers = ($coreFiles | Measure-Object PublicMembers -Sum).Sum
Add-Line ("public types   : {0}" -f $coreTypes)
Add-Line ("public members : {0}" -f $coreMembers)
Add-Line ""

Add-Line "-- ServiceLocator --"
Add-Line ("IsRegistered sites : {0} total, {1} in src" -f $isRegisteredAll, $isRegisteredSrc)
Add-Line ""

Add-Line "-- longest methods --"
foreach ($m in ($allMethods | Sort-Object Length -Descending | Select-Object -First $Top)) {
    Add-Line ("{0,5}  {1}:{2} {3}" -f $m.Length, $m.File, $m.Line, $m.Name)
}
Add-Line ""

Add-Line "-- deepest nesting inside a method --"
foreach ($m in ($allMethods | Sort-Object Nesting, Length -Descending | Select-Object -First $Top)) {
    Add-Line ("{0,5}  {1}:{2} {3}" -f $m.Nesting, $m.File, $m.Line, $m.Name)
}
Add-Line ""

Add-Line "-- most usings per file --"
foreach ($m in ($measured | Sort-Object Usings -Descending | Select-Object -First $Top)) {
    Add-Line ("{0,5}  {1}" -f $m.Usings, $m.Path)
}
Add-Line ""

Add-Line "-- totals --"
Add-Line ("methods parsed : {0}" -f $allMethods.Count)
Add-Line ("median method  : {0} lines" -f ([int]($allMethods | Sort-Object Length | Select-Object -Index ([int]($allMethods.Count / 2))).Length))

$text = $report -join [Environment]::NewLine
Write-Output $text

if ($Out) {
    Set-Content -LiteralPath $Out -Value $text -Encoding utf8
    Write-Output ""
    Write-Output ("written to " + $Out)
}
