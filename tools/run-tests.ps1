<#
.SYNOPSIS
    Runs the Core tests and appends one line about the run to the batch journal.

.DESCRIPTION
    A thin wrapper around `dotnet test` for check.bat. The test output goes to
    the console exactly as before; afterwards one line goes to
    artifacts\test-runs.tsv - when, how many passed out of how many, how long
    it took, ok or fail. The harness (`selfcheck`, `run`) writes the same file
    in the same format (RunJournal.cs); docs/QA.md, the batch-journal section.

    The counts come from the VSTest summary line
    ("Passed!  - Failed: 0, Passed: 1043, Skipped: 0, Total: 1043, ...");
    several test projects would add up. A run that died before reporting is
    recorded as 0 of 0 with status "fail".

    Output is kept ASCII on purpose: this runs from check.bat under cmd,
    whose console codepage is not UTF-8.

    Exit code is dotnet test's own.
#>
[CmdletBinding()]
param(
    [string] $Root,
    [string] $Solution = 'Wander.slnx'
)

$ErrorActionPreference = 'Continue'

# Not a param default: $PSScriptRoot is not populated yet while defaults are
# being evaluated in Windows PowerShell 5.1.
if (-not $Root) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$journal = Join-Path $Root 'artifacts\test-runs.tsv'

function Write-Journal([string] $path, [string] $batch, [int] $passed, [int] $total, [double] $seconds, [string] $status) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }
    # Without BOM, and the same header the harness writes: two writers, one file.
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    if (-not (Test-Path -LiteralPath $path)) {
        [System.IO.File]::AppendAllText($path, "when`tbatch`tpassed`ttotal`tseconds`tstatus`r`n", $utf8)
    }
    $when = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $sec = $seconds.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture)
    $line = "{0}`t{1}`t{2}`t{3}`t{4}`t{5}`r`n" -f $when, $batch, $passed, $total, $sec, $status
    [System.IO.File]::AppendAllText($path, $line, $utf8)
}

$script:passed = 0
$script:total = 0
$clock = [System.Diagnostics.Stopwatch]::StartNew()

# The summary line is localized (a Russian system prints it in Russian);
# English for this one process keeps the regex a single line. Environment
# variables set here die with the script.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

& $dotnet test (Join-Path $Root $Solution) --nologo --verbosity minimal --no-restore --no-build | ForEach-Object {
    $line = [string] $_
    Write-Output $line
    if ($line -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
        $script:passed += [int] $matches[2]
        $script:total += [int] $matches[4]
    }
}
$code = $LASTEXITCODE
$clock.Stop()

$status = if ($code -eq 0) { 'ok' } else { 'fail' }
Write-Journal $journal 'tests' $script:passed $script:total $clock.Elapsed.TotalSeconds $status
$sec = $clock.Elapsed.TotalSeconds.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture)
Write-Output ("  journal: {0} of {1} in {2} s -> artifacts\test-runs.tsv" -f $script:passed, $script:total, $sec)

exit $code
