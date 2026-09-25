<#
.SYNOPSIS
    Checks the extensions on the guide's formats page against the code.

.DESCRIPTION
    docs/GUIDE.md has one table of every format Wander handles (the section
    after the "### Formats" heading, in Russian). The same extensions live
    in Core as sets the preview router, the gallery, the actions and the
    content search decide by. A format added in code and not on the page is
    a feature nobody hears about; one on the page and in no set is a promise
    the code does not keep. Run at release preparation (docs/PRERELEASE.md,
    step 1), not by check.bat: the page is reconciled once per release.

    Two directions, like check-strings.ps1:
      * every extension in a code span on the page is in some set;
      * every extension in a set is on the page.

    The sets are read out of the source by name - their initializer
    between "= new ... {" and "};" - plus the single-extension routes of
    PreviewRouter (ext.Equals(".lnk")) and the companion rules. A set that
    is no longer where this script looks is an error too: the check must
    not go quietly blind after a rename.

    Output is kept ASCII on purpose, and so is this file (PowerShell 5.1
    reads it as ANSI): the page heading is spelled in code points.

    Exit code 0 when page and code agree, 1 otherwise.
#>
[CmdletBinding()]
param(
    [string] $Root
)

$ErrorActionPreference = 'Stop'

# Not a param default: $PSScriptRoot is not populated yet while defaults are
# being evaluated in Windows PowerShell 5.1.
if (-not $Root) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$sets = @(
    @{ File = 'src\Wander.Core\Icons\ImageFormats.cs'; Names = @('Raw', 'All', 'Heif') },
    @{ File = 'src\Wander.Core\Preview\PreviewRouter.cs'; Names = @('_animation', '_video', '_text', '_code', '_maybeText', '_web', '_executable', '_documentText') },
    @{ File = 'src\Wander.Core\Preview\AudioTags.cs'; Names = @('Extensions') },
    @{ File = 'src\Wander.Core\Preview\MeshFile.cs'; Names = @('Extensions') },
    @{ File = 'src\Wander.Core\Preview\BookCover.cs'; Names = @('_extensions') },
    @{ File = 'src\Wander.Core\Actions\FileTypeGroups.cs'; Names = @('Documents', 'Archives') }
)
# Routes and rules that name one extension at a time rather than a set.
$singles = @(
    @{ File = 'src\Wander.Core\Preview\PreviewRouter.cs'; Pattern = 'Equals\("(\.[A-Za-z0-9]+)"'; Label = 'PreviewRouter.ForExtension' },
    @{ File = 'src\Wander.Core\Companions\CompanionResolver.cs'; Pattern = 'new CompanionRule\("(\.[A-Za-z0-9]+)"'; Label = 'CompanionResolver' }
)

$problems = 0
# Extension -> the sets it was found in, for the report.
$code = @{}

function Add-Code([string] $ext, [string] $label) {
    $key = $ext.ToLowerInvariant()
    if (-not $code.ContainsKey($key)) {
        $code[$key] = [System.Collections.Generic.List[string]]::new()
    }
    $code[$key].Add($label)
}

foreach ($set in $sets) {
    $path = Join-Path $Root $set.File
    $text = Get-Content -Raw -Encoding UTF8 $path
    $class = [System.IO.Path]::GetFileNameWithoutExtension($path)
    foreach ($name in $set.Names) {
        $m = [regex]::Match($text, "(?s)\b$([regex]::Escape($name))\s*=\s*new\b[^{;]*\{(.*?)\};")
        if (-not $m.Success) {
            Write-Host "  $class.$name not found in $($set.File) - update this script" -ForegroundColor Red
            $problems++
            continue
        }
        foreach ($lit in [regex]::Matches($m.Groups[1].Value, '"(\.[A-Za-z0-9]+)"')) {
            Add-Code $lit.Groups[1].Value "$class.$name"
        }
    }
}
foreach ($single in $singles) {
    $text = Get-Content -Raw -Encoding UTF8 (Join-Path $Root $single.File)
    $found = [regex]::Matches($text, $single.Pattern)
    if ($found.Count -eq 0) {
        Write-Host "  nothing matched in $($single.File) - update this script" -ForegroundColor Red
        $problems++
    }
    foreach ($lit in $found) {
        Add-Code $lit.Groups[1].Value $single.Label
    }
}

# The page: from its heading to the next ## or ### (its own #### stay in).
$guide = Get-Content -Raw -Encoding UTF8 (Join-Path $Root 'docs\GUIDE.md')
# The Russian heading spelled in code points: literals in this file stay ASCII.
$heading = -join [char[]] @(0x424, 0x43E, 0x440, 0x43C, 0x430, 0x442, 0x44B)
$start = [regex]::Match($guide, "(?m)^### $heading\s*$")
if (-not $start.Success) {
    Write-Host "  formats page not found in docs\GUIDE.md - update this script" -ForegroundColor Red
    exit 1
}
$rest = $guide.Substring($start.Index + $start.Length)
$end = [regex]::Match($rest, '(?m)^#{2,3} ')
$page = if ($end.Success) { $rest.Substring(0, $end.Index) } else { $rest }

$onPage = [System.Collections.Generic.HashSet[string]]::new()
foreach ($m in [regex]::Matches($page, '`(\.[A-Za-z0-9][A-Za-z0-9.]*)`')) {
    [void] $onPage.Add($m.Groups[1].Value.ToLowerInvariant())
}

$pageOnly = @($onPage | Where-Object { -not $code.ContainsKey($_) } | Sort-Object)
$codeOnly = @($code.Keys | Where-Object { -not $onPage.Contains($_) } | Sort-Object)

if ($pageOnly.Count -gt 0) {
    Write-Host "  on the formats page, in no set:" -ForegroundColor Red
    $pageOnly | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
}
if ($codeOnly.Count -gt 0) {
    Write-Host "  in code, not on the formats page:" -ForegroundColor Yellow
    $codeOnly | ForEach-Object { Write-Host ("    {0,-10} {1}" -f $_, ($code[$_] -join ', ')) -ForegroundColor Yellow }
}

if ($problems -eq 0 -and $pageOnly.Count -eq 0 -and $codeOnly.Count -eq 0) {
    Write-Host "  $($onPage.Count) extensions on the page, all in code and back"
    exit 0
}

exit 1
