@echo off
setlocal enabledelayedexpansion
echo Cleaning up voice processes...

REM Kill voiceSample1 processes
taskkill /F /IM "voiceSample1.exe" 2>nul
if %ERRORLEVEL% EQU 0 (
    echo ✓ Killed voiceSample1.exe processes
) else (
    echo ℹ No voiceSample1.exe processes found
)

REM Kill semanticKernelSample1 processes  
taskkill /F /IM "semanticKernelSample1.exe" 2>nul
if %ERRORLEVEL% EQU 0 (
    echo ✓ Killed semanticKernelSample1.exe processes
) else (
    echo ℹ No semanticKernelSample1.exe processes found
)

REM Kill dotnet processes that might be running our app
for /f "tokens=2" %%i in ('tasklist /FI "IMAGENAME eq dotnet.exe" /FO CSV /NH 2^>nul ^| findstr /V "INFO:"') do (
    set PID=%%i
    set PID=!PID:"=!
    if defined PID (
        REM Check if this dotnet process is running our voice app
        wmic process where "ProcessId=!PID!" get CommandLine /format:value 2>nul | findstr /I "voiceSample1\|semanticKernelSample1" >nul
        if !ERRORLEVEL! EQU 0 (
            echo ✓ Killing dotnet process PID !PID! running voice app
            taskkill /F /PID !PID! 2>nul
        )
    )
)

echo Process cleanup completed.
echo.
