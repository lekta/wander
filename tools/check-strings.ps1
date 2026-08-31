<#
.SYNOPSIS
    Checks that the resource keys used in code match Strings.resx.

.DESCRIPTION
    Wander's user-facing text lives in src/Wander.App/Resources/Strings.resx,
    while the keys are spread across C# and XAML. A typo in a key compiles
    cleanly and only shows up in the UI, where the key itself appears in
    place of a label. The tests cannot cover this: they cover Wander.Core,
    and this is the app layer.

    Two things are verified:
      * every key the code asks for exists in the resx;
      * every key in the resx is used by somebody (otherwise it is dead).

    Keys are collected from four places:
      * Strings.<Key>           - C#;
      * res:Strings.<Key>       - XAML ({x:Static});
      * Text.Get / Text.Format  - Wander.Core, through ITextSource;
      * "MenuCmd..." / "Scope..."- the key tables in ContextMenuCatalog
                                  and ShellScopes.

    Output is kept ASCII on purpose: this runs from check.bat under cmd,
    whose console codepage is not UTF-8.

    Exit code 0 when everything lines up, 1 otherwise.
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

$resxPath = Join-Path $Root 'src\Wander.App\Resources\Strings.resx'
if (-not (Test-Path $resxPath)) {
    Write-Host "  check-strings: $resxPath not found" -ForegroundColor Red
    exit 1
}

$resx = Get-Content -Raw -Encoding UTF8 $resxPath
$defined = [System.Collections.Generic.HashSet[string]]::new()
foreach ($m in [regex]::Matches($resx, '<data name="([^"]+)"')) {
    [void] $defined.Add($m.Groups[1].Value)
}

$used = [System.Collections.Generic.HashSet[string]]::new()

# Every source file in the projects, minus generated output and the accessor
# itself (there a key sits on every line: it is the list, not a consumer).
$sources = Get-ChildItem -Path (Join-Path $Root 'src') -Recurse -Include *.cs, *.xaml |
    Where-Object {
        $_.FullName -notmatch '\\(obj|bin)\\' -and
        $_.FullName -notmatch '\\Resources\\Strings(\.[A-Za-z]+)?\.cs$'
    }

foreach ($file in $sources) {
    $text = Get-Content -Raw -Encoding UTF8 $file.FullName

    foreach ($m in [regex]::Matches($text, '(?:res:)?\bStrings\.([A-Za-z][A-Za-z0-9_]*)')) {
        [void] $used.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($text, 'Text\.(?:Get|Format)\("([^"]+)"')) {
        [void] $used.Add($m.Groups[1].Value)
    }
    # PathSafety funnels Text.Get through local Say/Fill helpers.
    foreach ($m in [regex]::Matches($text, '\b(?:Say|Fill)\("([^"]+)"')) {
        [void] $used.Add($m.Groups[1].Value)
    }
    # Core's own key tables: a dictionary literal from enum/name to resource
    # key. Both live in Core, which cannot reference the accessor at all.
    foreach ($m in [regex]::Matches($text, '= "((?:MenuCmd|Scope)[A-Za-z]+)"')) {
        [void] $used.Add($m.Groups[1].Value)
    }
}

# Methods on the accessor, not keys.
[void] $used.Remove('Get')
[void] $used.Remove('Format')
# The file name as mentioned in comments: "Strings.resx".
[void] $used.Remove('resx')

$missing = @($used | Where-Object { -not $defined.Contains($_) } | Sort-Object)
$unused = @($defined | Where-Object { -not $used.Contains($_) } | Sort-Object)

if ($missing.Count -eq 0 -and $unused.Count -eq 0) {
    Write-Host "  $($defined.Count) keys, all accounted for"
    exit 0
}

if ($missing.Count -gt 0) {
    Write-Host "  missing from Strings.resx (the UI will show the key itself):" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
}
if ($unused.Count -gt 0) {
    Write-Host "  in Strings.resx but used nowhere:" -ForegroundColor Yellow
    $unused | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}

exit 1
