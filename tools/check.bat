@echo off
rem ===========================================================
rem  Wander - combined verification entry point.
rem
rem    tools\check.bat            build + format verify + strings + tests
rem    tools\check.bat run        + smoke launch of the app
rem    tools\check.bat format     apply dotnet format (writes files)
rem    tools\check.bat qa         build + harness selfcheck + smoke-walk
rem
rem  Exit code 0 on success, non-zero on the first failed step.
rem ===========================================================
setlocal enabledelayedexpansion

set DOTNET="C:\Program Files\dotnet\dotnet.exe"
set SLN=Wander.slnx
rem  One place, because it drifted once: the app moved to a versioned Windows
rem  TFM and the smoke path below kept pointing at the old folder, so
rem  `check.bat run` reported "smoke launch FAILED" for a path that simply
rem  did not exist any more.
set TFM=net10.0-windows10.0.19041.0
set HARNESS=tests\Wander.Harness\bin\Debug\%TFM%\Wander.Harness.exe
set MODE=%1
if "%MODE%"=="" set MODE=check

if /i "%MODE%"=="format" (
    %DOTNET% format %SLN% --no-restore
    exit /b !errorlevel!
)

rem  The window harness: minutes rather than seconds, because it generates a
rem  sandbox and drives the real application through it. Deliberately not
rem  part of the ordinary check - see docs/QA.md, "Автоматизация".
if /i "%MODE%"=="qa" (
    echo === build ===
    %DOTNET% build %SLN% --nologo
    if errorlevel 1 exit /b 1

    echo.
    echo === harness selfcheck ===
    rem Generators against the readers the app uses. Cheap, and it is what
    rem tells a broken scenario from a malformed test file.
    "%~dp0..\%HARNESS%" selfcheck
    if errorlevel 1 exit /b 1

    echo.
    echo === harness smoke-walk ===
    "%~dp0..\%HARNESS%" run "%~dp0..\tests\Wander.Harness\Scenarios\smoke-walk.json"
    rem The harness answers with its exit code: 0 passed, 2 a step failed,
    rem 70 the harness itself fell over. The path to report.md is the last
    rem line it prints, and it is the thing to read either way.
    if !errorlevel! neq 0 (
        echo   harness FAILED - read the report above
        exit /b 1
    )

    echo.
    echo OK
    exit /b 0
)

echo === build ===
%DOTNET% build %SLN% --nologo
if errorlevel 1 exit /b 1

echo.
echo === format verify ===
%DOTNET% format %SLN% --verify-no-changes --no-restore
if errorlevel 1 exit /b 1

echo.
echo === strings ===
rem Resource keys against Strings.resx. The tests cannot reach this (they
rem cover Wander.Core only), and a typo in a key shows up nowhere except as
rem the key itself appearing in the UI in place of a label.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check-strings.ps1"
if errorlevel 1 exit /b 1

echo.
echo === tests ===
rem Through a script rather than `dotnet test` directly: same command, plus
rem one line per run in artifacts\test-runs.tsv - passed of total and the
rem time it took. The harness writes the same file (docs/QA.md).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-tests.ps1"
if errorlevel 1 exit /b 1

if /i "%MODE%"=="run" (
    echo.
    echo === smoke run ===
    set EXE=src\Wander.App\bin\Debug\%TFM%\Wander.exe
    rem Its own data root, not %LOCALAPPDATA%\Wander: a smoke run opens a
    rem log and writes state.json like any other launch, and a check that
    rem runs a dozen times a day left a dozen two-second sessions in the
    rem real logs folder - and overwrote the real state.json on the way.
    set SMOKEDATA=%~dp0..\artifacts\smoke
    if exist "!SMOKEDATA!" rd /s /q "!SMOKEDATA!"
    rem --smoke keeps the window off-screen and closes it after a couple of
    rem seconds, and the exit code is the answer. Called directly rather than
    rem through `start`, so this batch waits for it and can read that code —
    rem the previous version launched, slept and killed, and printed "ok"
    rem even when the app had already died on startup.
    rem The existence check is not belt and braces: launching a path that is
    rem not there fails with cmd's own message and "see the logs", and the
    rem logs of a process that never started say nothing.
    if not exist "!EXE!" (
        echo   smoke launch FAILED - no such file: !EXE!
        set SMOKE_FAILED=1
    ) else (
        "!EXE!" --smoke --data-dir "!SMOKEDATA!\data"
        rem `if errorlevel 1` compares as signed and misses a .NET crash,
        rem which exits with 0xE0434352 — negative as an int32. `neq 0`
        rem catches both.
        if !errorlevel! neq 0 (
            set SMOKE_FAILED=1
        ) else (
            echo   smoke launch ok
            rem Green leaves nothing behind; red keeps the folder, because
            rem the log inside it is what the message below points at.
            rd /s /q "!SMOKEDATA!"
        )
    )
)

rem Reported out here, not inside the block above: `exit /b` from within a
rem parenthesised block leaves cmd's own exit code at whatever the last
rem command in it set — the echo — and check.bat is judged by that code.
if defined SMOKE_FAILED (
    echo   smoke launch FAILED - see artifacts\smoke\data\logs
    exit /b 1
)

echo.
echo OK
exit /b 0
