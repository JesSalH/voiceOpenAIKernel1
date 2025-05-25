@echo off
echo Checking if SoX is installed...

where sox >nul 2>&1
if %errorlevel% equ 0 (
    echo SoX is already installed!
    goto :EOF
)

echo SoX not found. Trying to install...

:: Check if Chocolatey is installed
where choco >nul 2>&1
if %errorlevel% equ 0 (
    echo Chocolatey is installed. Installing SoX...
    choco install sox.portable -y
    if %errorlevel% equ 0 (
        echo SoX installed successfully with Chocolatey!
        echo Please restart your command prompt or PowerShell for the PATH changes to take effect.
        goto :EOF
    ) else (
        echo Failed to install SoX with Chocolatey.
    )
) else (
    echo Chocolatey is not installed.
)

echo.
echo SoX could not be automatically installed.
echo.
echo Please install SoX manually:
echo 1. Download SoX from https://sourceforge.net/projects/sox/files/latest/download
echo 2. Extract the files and add the SoX directory to your PATH environment variable
echo.
echo After installing, restart this application.
echo.
pause
