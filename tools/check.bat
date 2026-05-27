@echo off
rem ===========================================================
rem  Wander - combined verification entry point.
rem
rem    tools\check.bat            build + format verify + tests
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
echo === tests ===
%DOTNET% test %SLN% --nologo --verbosity minimal --no-restore --no-build
if errorlevel 1 exit /b 1

if /i "%MODE%"=="run" (
    echo.
    echo === smoke run ===
    set EXE=src\Wander.App\bin\Debug\net10.0-windows\Wander.App.exe
    start "" "!EXE!"
    timeout /t 5 /nobreak > nul
    taskkill /f /im Wander.App.exe > nul 2>&1
    echo   smoke launch ok
)

echo.
echo OK
exit /b 0
