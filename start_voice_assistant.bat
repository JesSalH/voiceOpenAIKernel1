@echo off
echo Starting Voice Assistant...
echo.

REM Kill any existing voice processes first
echo Cleaning up any running voice processes...
powershell -ExecutionPolicy Bypass -File "%~dp0kill_voice_processes.ps1"

echo.
echo Building and running the application...
echo.

REM Build the project
dotnet build "%~dp0voiceSample1.csproj"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build failed! Press any key to exit...
    pause >nul
    exit /b %ERRORLEVEL%
)

echo.
echo Build successful! Starting the voice assistant...
echo.

REM Run the application
dotnet run --project "%~dp0voiceSample1.csproj"

echo.
echo Application finished. Press any key to exit...
pause >nul
