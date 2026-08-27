@echo off
rem ===========================================================
rem  Wander - combined verification entry point.
rem
rem    tools\check.bat            build + format verify + strings + tests
rem    tools\check.bat run        + smoke launch of the app
rem    tools\check.bat format     apply dotnet format (writes files)
rem
rem  Exit code 0 on success, non-zero on the first failed step.
rem ===========================================================
setlocal enabledelayedexpansion

set DOTNET="C:\Program Files\dotnet\dotnet.exe"
set SLN=Wander.slnx
set MODE=%1
if "%MODE%"=="" set MODE=check

if /i "%MODE%"=="format" (
    %DOTNET% format %SLN% --no-restore
    exit /b !errorlevel!
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
%DOTNET% test %SLN% --nologo --verbosity minimal --no-restore --no-build
if errorlevel 1 exit /b 1

if /i "%MODE%"=="run" (
    echo.
    echo === smoke run ===
    set EXE=src\Wander.App\bin\Debug\net10.0-windows\Wander.exe
    rem --smoke keeps the window off-screen and closes it after a couple of
    rem seconds, and the exit code is the answer. Called directly rather than
    rem through `start`, so this batch waits for it and can read that code —
    rem the previous version launched, slept and killed, and printed "ok"
    rem even when the app had already died on startup.
    "!EXE!" --smoke
    rem `if errorlevel 1` compares as signed and misses a .NET crash, which
    rem exits with 0xE0434352 — negative as an int32. `neq 0` catches both.
    if !errorlevel! neq 0 (
        set SMOKE_FAILED=1
    ) else (
        echo   smoke launch ok
    )
)

rem Reported out here, not inside the block above: `exit /b` from within a
rem parenthesised block leaves cmd's own exit code at whatever the last
rem command in it set — the echo — and check.bat is judged by that code.
if defined SMOKE_FAILED (
    echo   smoke launch FAILED - see %LOCALAPPDATA%\Wander\logs
    exit /b 1
)

echo.
echo OK
exit /b 0
