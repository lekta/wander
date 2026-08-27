# Из чего состоит портативный Wander.exe.
#
# Одиночный self-contained exe — это ровно тот набор файлов, который даёт
# обычная публикация в папку, сложенный в один бандл и сжатый. Скрипт
# публикует этот набор во временную папку и считает, сколько в нём весит
# каждая категория — распакованной и сжатой (gzip, так что проценты сходятся
# с настоящим exe в пределах пары процентов).
#
# Запуск из корня репозитория:
#
#     .\tools\size-report.ps1
#
# Ключ -Keep оставляет опубликованную папку на диске, чтобы посмотреть на
# отдельные файлы руками.
#
# Вывод по-английски намеренно — как в publish.ps1: без BOM Windows
# PowerShell 5.1 читает файл как ANSI и на кириллице в строках падает.

[CmdletBinding()]
param(
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$outDir = Join-Path ([System.IO.Path]::GetTempPath()) "wander-size-$PID"

Write-Host "Publishing the file set (Release, win-x64, self-contained, folder)..." -ForegroundColor Cyan
dotnet publish src\Wander.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o $outDir --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Категории — по имени файла, потому что состав рантайма именами и задан.
# Папка берётся от самого файла, а не вычитанием корня из полного пути:
# короткие (8.3) имена в TEMP делают такую арифметику неверной молча.
function Get-Category([string]$name, [string]$folder) {
    if ($folder -and $folder -ne 'runtimes' -and $folder -match '^[a-z]{2}(-[A-Za-z]+)?$') {
        return '.NET localisation satellites'
    }
    switch -Regex ($name) {
        '^Wander\.' { return 'Wander code' }
        '^(AvalonEdit|Markdig|MetadataExtractor|XmpCore|Microsoft\.Web\.WebView2|WebView2Loader|System\.Drawing\.Common)' {
            return 'Wander NuGet deps'
        }
        '^Microsoft\.Windows\.SDK\.NET' { return 'WinRT projection (PDF render)' }
        'Windows\.Forms|^System\.Private\.Windows' { return 'WinForms (unused)' }
        '^(mscordaccore|mscordbi|Microsoft\.DiaSymReader|createdump)' { return 'Debugging components' }
        '^(PresentationFramework|PresentationCore|PresentationNative|PresentationUI|WindowsBase|System\.Xaml|wpfgfx|D3DCompiler|ReachFramework|System\.Printing|UIAutomation|DirectWriteForwarder|PenImc|vcruntime|System\.Windows\.Controls\.Ribbon|WindowsFormsIntegration)' {
            return 'WPF'
        }
        '^(coreclr|clrjit|clretwrc|hostfxr|hostpolicy|System\.Private\.CoreLib|msquic|ucrtbase|System\.IO\.Compression\.Native)' {
            return 'CLR core'
        }
    }
    return 'Rest of the BCL (System.*)'
}

function Get-CompressedSize([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $mem = New-Object System.IO.MemoryStream
    $gz = New-Object System.IO.Compression.GZipStream -ArgumentList $mem, ([System.IO.Compression.CompressionLevel]::Optimal)
    $gz.Write($bytes, 0, $bytes.Length)
    # Закрыть поток до замера обязательно: gzip дописывает хвост на Dispose.
    # ToArray() у MemoryStream разрешён и после закрытия, Length — уже нет.
    $gz.Dispose()
    return $mem.ToArray().Length
}

$rootFull = (Get-Item $outDir).FullName.TrimEnd([char]92)
$buckets = @{}
foreach ($file in Get-ChildItem $outDir -Recurse -File) {
    # Пустая строка для файлов в корне публикации, иначе — имя папки,
    # в которой файл лежит ("ru", "runtimes", ...).
    $folder = if ($file.Directory.FullName -eq $rootFull) { '' } else { $file.Directory.Name }
    $cat = Get-Category $file.Name $folder
    if (-not $buckets.ContainsKey($cat)) {
        $buckets[$cat] = [pscustomobject]@{ Raw = [long]0; Packed = [long]0; Files = 0 }
    }
    $buckets[$cat].Raw += $file.Length
    $buckets[$cat].Packed += (Get-CompressedSize $file.FullName)
    $buckets[$cat].Files += 1
}

$totalPacked = ($buckets.Values | Measure-Object -Property Packed -Sum).Sum
$totalRaw = ($buckets.Values | Measure-Object -Property Raw -Sum).Sum

Write-Host ""
$buckets.GetEnumerator() | Sort-Object { -$_.Value.Packed } | ForEach-Object {
    [pscustomobject]@{
        'Category'      = $_.Key
        'Raw, MB'       = [math]::Round($_.Value.Raw / 1MB, 1)
        'Packed, MB'    = [math]::Round($_.Value.Packed / 1MB, 1)
        'Share'         = "{0:N1} %" -f (100 * $_.Value.Packed / $totalPacked)
        'Files'         = $_.Value.Files
    }
} | Format-Table -AutoSize

Write-Host ("TOTAL: {0:N1} MB raw -> {1:N1} MB packed" -f ($totalRaw / 1MB), ($totalPacked / 1MB))
Write-Host "(the real exe is slightly bigger - the bundle has headers of its own)"

if ($Keep) {
    Write-Host ""
    Write-Host "File set kept at: $outDir"
} else {
    Remove-Item $outDir -Recurse -Force
}
