<#
.SYNOPSIS
    Checks that every XAML resource key the app asks for is defined somewhere.

.DESCRIPTION
    Brushes, styles and converters are looked up by key, and a typo in one
    compiles cleanly: a StaticResource that finds nothing fails at run time,
    when the view that holds it is first built - a settings page nobody
    opened in a smoke run - and a DynamicResource fails silently, leaving
    the default look in place.

    Definitions are collected from every x:Key="..." in the app's XAML
    (Palette, MenuStyles, the windows' and views' own dictionaries), plus
    the keys code puts in place at run time: Resources["X"] = value.

    Uses are collected from:
      * {StaticResource X} and {DynamicResource X}, with or without
        ResourceKey=, and <StaticResource ResourceKey="X"/> - XAML;
      * FindResource("X"), TryFindResource("X"), a read of Resources["X"],
        and Palette's own Find("X") - C#.

    Keys given as markup ({x:Type ...}, {x:Static ...} - the system's own
    keys among them) are not names and are skipped. Scope is not
    analysed on purpose: a key defined in one window and used in another
    passes here and fails at run time. This catches typos, not scope.

    Keys defined and used nowhere are listed but do not fail the step (yet).

    Output is kept ASCII on purpose: this runs from check.bat under cmd,
    whose console codepage is not UTF-8.

    Exit code 0 when every used key is defined, 1 otherwise.
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

$appRoot = Join-Path $Root 'src\Wander.App'
if (-not (Test-Path $appRoot)) {
    Write-Host "  check-resources: $appRoot not found" -ForegroundColor Red
    exit 1
}

$files = Get-ChildItem -Path $appRoot -Recurse -Include *.cs, *.xaml |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

$defined = [System.Collections.Generic.HashSet[string]]::new()
$used = [System.Collections.Generic.HashSet[string]]::new()

$name = '[A-Za-z_][A-Za-z0-9_]*'

foreach ($file in $files) {
    $text = Get-Content -Raw -Encoding UTF8 $file.FullName

    if ($file.Extension -eq '.xaml') {
        foreach ($m in [regex]::Matches($text, "x:Key=""($name)""")) {
            [void] $defined.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($text, "\{(?:StaticResource|DynamicResource)\s+(?:ResourceKey=)?($name)\s*\}")) {
            [void] $used.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($text, "<(?:StaticResource|DynamicResource)\s+ResourceKey=""($name)""")) {
            [void] $used.Add($m.Groups[1].Value)
        }

        continue
    }

    # An assignment defines the key for the XAML that refers to it; any
    # other mention of Resources["X"] reads it.
    foreach ($m in [regex]::Matches($text, "\bResources\[""($name)""\](\s*=(?!=))?")) {
        if ($m.Groups[2].Success) {
            [void] $defined.Add($m.Groups[1].Value)
        } else {
            [void] $used.Add($m.Groups[1].Value)
        }
    }
    foreach ($m in [regex]::Matches($text, "\b(?:Try)?FindResource\(""($name)""\)")) {
        [void] $used.Add($m.Groups[1].Value)
    }
    # Palette funnels FindResource through a local Find helper.
    if ($file.Name -eq 'Palette.cs') {
        foreach ($m in [regex]::Matches($text, "\bFind\(""($name)""\)")) {
            [void] $used.Add($m.Groups[1].Value)
        }
    }
}

$missing = @($used | Where-Object { -not $defined.Contains($_) } | Sort-Object)
$unused = @($defined | Where-Object { -not $used.Contains($_) } | Sort-Object)

if ($missing.Count -gt 0) {
    Write-Host "  used but defined nowhere (fails when the view is built):" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
}
if ($unused.Count -gt 0) {
    Write-Host "  defined but used nowhere (not a failure):" -ForegroundColor Yellow
    $unused | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}

if ($missing.Count -gt 0) {
    exit 1
}

Write-Host "  $($defined.Count) keys defined, $($used.Count) used, all found"
exit 0
