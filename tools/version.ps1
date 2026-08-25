# Поднимает версию Wander во всех местах, где она продублирована.
#
#   tools\version.ps1 0.3.0              -> 0.3.0-beta (суффикс по умолчанию)
#   tools\version.ps1 0.3.0 -Suffix rc1  -> 0.3.0-rc1
#   tools\version.ps1 1.0.0 -Suffix ''   -> 1.0.0 без суффикса
#   tools\version.ps1 0.3.0 -DryRun      -> показать, ничего не записывая
#
# Трогает только файлы. Git-команды (commit, tag, push) НЕ выполняет —
# печатает их в конце, запускать вручную.
#
# ВНИМАНИЕ: у файла нет UTF-8 BOM, а Windows PowerShell 5.1 без BOM читает
# .ps1 как ANSI. Поэтому кириллица здесь допустима ТОЛЬКО в комментариях
# (их парсер пропускает до конца строки). Все строковые литералы и вывод —
# ASCII/английский, иначе скрипт развалится на разборе. Тот же расклад,
# что в publish.ps1.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [AllowEmptyString()]
    [string]$Suffix = 'beta',

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be MAJOR.MINOR.PATCH (e.g. 0.3.0), got '$Version'."
}
if ($Suffix -and $Suffix -notmatch '^[0-9A-Za-z.-]+$') {
    throw "Suffix may contain only letters, digits, dot and dash, got '$Suffix'."
}

$repoRoot      = Split-Path -Parent $PSScriptRoot
$propsPath     = Join-Path $repoRoot 'Directory.Build.props'
$changelogPath = Join-Path $repoRoot 'docs\CHANGELOG.md'

foreach ($p in @($propsPath, $changelogPath)) {
    if (-not (Test-Path $p)) { throw "File not found: $p" }
}

# Четыре формы одной версии — намеренно разные, см. docs/RELEASING.md.
$informational = if ($Suffix) { "$Version-$Suffix" } else { $Version }
$fileVersion   = "$Version.0"
$tag           = "v$Version"
$today         = (Get-Date).ToString('yyyy-MM-dd')
$releaseUrl    = "https://github.com/lekta/wander/releases/tag/$tag"

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Read-Text([string]$Path) {
    return [System.IO.File]::ReadAllText($Path)
}

function Write-Text([string]$Path, [string]$Text) {
    if ($DryRun) { return }
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

# Заменяет содержимое ровно одного XML-тега. Падает, если тег не найден или
# найден дважды — молча разъехавшаяся версия хуже явной ошибки.
function Set-XmlTagValue([string]$Text, [string]$Tag, [string]$Value) {
    $pattern = "(?<=<$Tag>)[^<]*(?=</$Tag>)"
    $found = [regex]::Matches($Text, $pattern)
    if ($found.Count -ne 1) {
        throw "Expected exactly one <$Tag> in Directory.Build.props, found $($found.Count)."
    }

    return [regex]::Replace($Text, $pattern, $Value)
}

# --- Directory.Build.props -------------------------------------------------

$props = Read-Text $propsPath
$oldVersion = ([regex]::Match($props, '(?<=<Version>)[^<]*(?=</Version>)')).Value

$props = Set-XmlTagValue $props 'Version'              $Version
$props = Set-XmlTagValue $props 'InformationalVersion' $informational
$props = Set-XmlTagValue $props 'FileVersion'          $fileVersion
# AssemblyVersion НЕ трогаем: это binding-идентичность сборки, приколочена
# к 0.0.0.0 намеренно. Подробнее — docs/RELEASING.md.

Write-Text $propsPath $props

# --- docs/CHANGELOG.md -----------------------------------------------------

$changelog = Read-Text $changelogPath

if ($changelog -match [regex]::Escape("## [$informational]")) {
    throw "CHANGELOG.md already has a [$informational] section - version not bumped."
}

$nl = if ($changelog -match "`r`n") { "`r`n" } else { "`n" }

$section = @(
    "## [$informational] - $today",
    '',
    '### Added',
    '- TODO',
    '',
    '### Fixed',
    '- TODO',
    ''
) -join $nl

# Новая секция идёт перед самой свежей из существующих.
$firstSection = [regex]::Match($changelog, '(?m)^## \[')
if (-not $firstSection.Success) {
    throw 'CHANGELOG.md has no "## [...]" section - check the file format.'
}
$changelog = $changelog.Insert($firstSection.Index, $section + $nl)

# Ссылка-сноска внизу файла, перед самой свежей из существующих.
$firstLink = [regex]::Match($changelog, '(?m)^\[[^\]]+\]:\s*http')
if (-not $firstLink.Success) {
    throw 'CHANGELOG.md has no "[x]: http..." link refs - check the file format.'
}
$changelog = $changelog.Insert($firstLink.Index, "[$informational]: $releaseUrl" + $nl)

Write-Text $changelogPath $changelog

# --- Итог ------------------------------------------------------------------

$mode = if ($DryRun) { ' (DRY RUN - nothing written)' } else { '' }
Write-Host ""
Write-Host "Version $oldVersion -> $Version$mode" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Directory.Build.props"
Write-Host "    Version              = $Version"
Write-Host "    InformationalVersion = $informational"
Write-Host "    FileVersion          = $fileVersion"
Write-Host "    AssemblyVersion      = 0.0.0.0  (left alone on purpose)" -ForegroundColor DarkGray
Write-Host "  docs/CHANGELOG.md"
Write-Host "    + section [$informational] - $today"
Write-Host "    + link ref -> $releaseUrl"
Write-Host ""
Write-Host "Next, by hand:" -ForegroundColor Yellow
Write-Host "  1. Fill in the TODOs in docs/CHANGELOG.md"
Write-Host "  2. .\tools\check.bat"
Write-Host "  3. git commit; git tag $tag; git push origin master --follow-tags"
Write-Host "     (pushing tag $tag triggers .github/workflows/release.yml)"
Write-Host ""
