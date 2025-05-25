# Check if sox is already installed
$soxInstalled = $false
try {
    $soxOutput = (Get-Command sox -ErrorAction Stop) 2>&1
    $soxInstalled = $true
    Write-Host "SoX is already installed at: $($soxOutput.Source)" -ForegroundColor Green
} catch {
    Write-Host "SoX not found in PATH" -ForegroundColor Yellow
}

if (-not $soxInstalled) {
    # Check if Chocolatey is installed
    $chocoInstalled = $false
    try {
        $chocoVersion = (choco -v) 2>&1
        $chocoInstalled = $true
        Write-Host "Chocolatey is installed: $chocoVersion" -ForegroundColor Green
    } catch {
        Write-Host "Chocolatey is not installed" -ForegroundColor Yellow
    }

    if (-not $chocoInstalled) {
        Write-Host "Installing Chocolatey..." -ForegroundColor Cyan
        try {
            Set-ExecutionPolicy Bypass -Scope Process -Force
            [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
            iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
            Write-Host "Chocolatey installed successfully" -ForegroundColor Green
        } catch {
            Write-Host "Failed to install Chocolatey: $_" -ForegroundColor Red
            Write-Host "Please install SoX manually from https://sourceforge.net/projects/sox/"
            exit 1
        }
    }

    # Install SoX using Chocolatey
    Write-Host "Installing SoX using Chocolatey..." -ForegroundColor Cyan
    try {
        choco install sox.portable -y
        Write-Host "SoX installed successfully!" -ForegroundColor Green
        Write-Host "Please restart your PowerShell session or terminal for the PATH changes to take effect" -ForegroundColor Yellow
    } catch {
        Write-Host "Failed to install SoX: $_" -ForegroundColor Red
        Write-Host "Please install SoX manually from https://sourceforge.net/projects/sox/"
        exit 1
    }
}

# Verify SoX installation
try {
    $soxVersion = (sox --version) 2>&1
    Write-Host "SoX version: $soxVersion" -ForegroundColor Green
    Write-Host "SoX is properly installed and ready to use" -ForegroundColor Green
} catch {
    Write-Host "SoX verification failed: $_" -ForegroundColor Red
    Write-Host "If you just installed SoX, you may need to restart your PowerShell session or terminal" -ForegroundColor Yellow
    exit 1
}
