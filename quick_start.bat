@echo off
echo.
echo ======================================
echo     Voice Assistant Quick Start
echo ======================================
echo.

REM Kill any existing processes
echo Cleaning up any running voice processes...
call "%~dp0kill_voice_processes_simple.bat"

echo.
echo Building and starting the Voice Assistant...
echo.

REM Build and run in one command
dotnet run --project "%~dp0voiceSample1.csproj"

echo.
echo Voice Assistant has stopped.
echo.

REM Clean up again after exit
echo Final cleanup...
call "%~dp0kill_voice_processes_simple.bat"

echo.
echo Press any key to exit...
pause >nul
