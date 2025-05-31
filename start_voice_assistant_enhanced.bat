@echo off
setlocal enabledelayedexpansion

echo.
echo ===================================================
echo           Voice Assistant Startup Script
echo ===================================================
echo.

REM Set the script directory
set SCRIPT_DIR=%~dp0

REM Step 1: Comprehensive cleanup
echo [1/4] Performing comprehensive process cleanup...
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%cleanup_voice_processes.ps1" -Verbose

REM Check if cleanup was successful
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ⚠ Warning: Process cleanup encountered issues, but continuing...
    echo.
)

REM Step 2: Clean build (remove bin/obj folders to ensure fresh build)
echo [2/4] Cleaning previous build artifacts...
if exist "%SCRIPT_DIR%bin" (
    echo   Removing bin folder...
    rmdir /s /q "%SCRIPT_DIR%bin" 2>nul
)
if exist "%SCRIPT_DIR%obj" (
    echo   Removing obj folder...
    rmdir /s /q "%SCRIPT_DIR%obj" 2>nul
)

REM Step 3: Build the project
echo [3/4] Building the project...
echo.
dotnet build "%SCRIPT_DIR%voiceSample1.csproj" --verbosity minimal

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ❌ Build failed! 
    echo.
    echo Attempting one more cleanup and rebuild...
    
    REM Try cleanup again
    powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%cleanup_voice_processes.ps1" -Force
    
    REM Wait a bit
    timeout /t 2 /nobreak >nul
    
    REM Try build again
    echo Retrying build...
    dotnet build "%SCRIPT_DIR%voiceSample1.csproj" --verbosity minimal
    
    if !ERRORLEVEL! NEQ 0 (
        echo.
        echo ❌ Build failed again! Please check for errors above.
        echo Press any key to exit...
        pause >nul
        exit /b !ERRORLEVEL!
    )
)

echo.
echo ✅ Build successful!

REM Step 4: Run the application  
echo [4/4] Starting the Voice Assistant...
echo.
echo ===================================================
echo        Voice Assistant is now starting...
echo ===================================================
echo.

dotnet run --project "%SCRIPT_DIR%voiceSample1.csproj"

REM Capture the exit code
set APP_EXIT_CODE=%ERRORLEVEL%

echo.
echo ===================================================
echo          Voice Assistant has stopped
echo ===================================================

REM Final cleanup
echo.
echo Performing final cleanup...
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%cleanup_voice_processes.ps1"

if %APP_EXIT_CODE% NEQ 0 (
    echo.
    echo ⚠ Application exited with code: %APP_EXIT_CODE%
)

echo.
echo Press any key to exit...
pause >nul

exit /b %APP_EXIT_CODE%
