# Enhanced PowerShell script for development - includes more comprehensive cleanup
param(
    [switch]$Verbose,
    [switch]$Force
)

if ($Verbose) {
    $VerbosePreference = "Continue"
}

Write-Host "=== Voice Assistant Process Cleanup ===" -ForegroundColor Cyan
Write-Host "Starting comprehensive cleanup of voice processes..." -ForegroundColor Yellow

# Extended list of process names to kill
$processesToKill = @(
    "voiceSample1",
    "semanticKernelSample1", 
    "GptAssistant",
    "VoiceHandler"
)

# Also kill dotnet processes running our specific applications
$dotnetProcessesToCheck = @(
    "voiceSample1",
    "semanticKernelSample1",
    "Assistant",
    "voice"
)

$killedProcesses = @()
$totalProcesses = 0

# Function to safely kill a process
function Kill-ProcessSafely {
    param($Process, $ProcessName)
    
    try {
        if ($Force) {
            Stop-Process -Id $Process.Id -Force -ErrorAction Stop
        }
        else {
            # Try graceful shutdown first
            $Process.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 100
            
            if (!$Process.HasExited) {
                Stop-Process -Id $Process.Id -Force -ErrorAction Stop
            }
        }
        
        return $true
    }
    catch {
        Write-Warning "Failed to kill process $ProcessName (PID: $($Process.Id)): $($_.Exception.Message)"
        return $false
    }
}

# Kill specific voice application processes
foreach ($processName in $processesToKill) {
    try {
        $processes = Get-Process -Name $processName -ErrorAction SilentlyContinue
        
        if ($processes) {
            foreach ($process in $processes) {
                $totalProcesses++
                Write-Host "Found process: $processName (PID: $($process.Id))" -ForegroundColor Yellow
                
                if (Kill-ProcessSafely -Process $process -ProcessName $processName) {
                    Write-Host "  ✓ Killed: $processName (PID: $($process.Id))" -ForegroundColor Red
                    $killedProcesses += "$processName (PID: $($process.Id))"
                }
            }
        }
    }
    catch {
        Write-Verbose "Error checking for process $processName : $($_.Exception.Message)"
    }
}

# Check dotnet processes for our applications
try {
    $dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
    
    if ($dotnetProcesses) {
        Write-Host "Checking dotnet processes..." -ForegroundColor Yellow
        
        foreach ($process in $dotnetProcesses) {
            try {
                # Get command line using WMI (more reliable than other methods)
                $wmiProcess = Get-WmiObject -Class Win32_Process -Filter "ProcessId = $($process.Id)" -ErrorAction SilentlyContinue
                
                if ($wmiProcess -and $wmiProcess.CommandLine) {
                    $commandLine = $wmiProcess.CommandLine.ToLower()
                    $shouldKill = $false
                    
                    foreach ($checkName in $dotnetProcessesToCheck) {
                        if ($commandLine -like "*$($checkName.ToLower())*") {
                            $shouldKill = $true
                            break
                        }
                    }
                    
                    if ($shouldKill) {
                        $totalProcesses++
                        Write-Host "Found dotnet process running voice app (PID: $($process.Id))" -ForegroundColor Yellow
                        Write-Verbose "Command line: $commandLine"
                        
                        if (Kill-ProcessSafely -Process $process -ProcessName "dotnet") {
                            Write-Host "  ✓ Killed: dotnet voice app (PID: $($process.Id))" -ForegroundColor Red
                            $killedProcesses += "dotnet voice app (PID: $($process.Id))"
                        }
                    }
                }
            }
            catch {
                Write-Verbose "Could not check dotnet process PID $($process.Id): $($_.Exception.Message)"
            }
        }
    }
}
catch {
    Write-Verbose "Error checking dotnet processes: $($_.Exception.Message)"
}

# Wait for processes to fully terminate
Write-Host "Waiting for processes to terminate..." -ForegroundColor Yellow
Start-Sleep -Milliseconds 1000

# Clean up any locked files in the bin directory
try {
    $binPath = Join-Path $PSScriptRoot "bin\Debug\net9.0"
    if (Test-Path $binPath) {
        Write-Host "Checking for locked files in bin directory..." -ForegroundColor Yellow
        
        $lockedFiles = @("voiceSample1.exe", "semanticKernelSample1.exe")
        foreach ($file in $lockedFiles) {
            $filePath = Join-Path $binPath $file
            if (Test-Path $filePath) {
                try {
                    # Try to access the file to see if it's locked
                    [System.IO.File]::OpenWrite($filePath).Close()
                    Write-Verbose "File $file is not locked"
                }
                catch {
                    Write-Host "  ⚠ File $file appears to be locked" -ForegroundColor Orange
                }
            }
        }
    }
}
catch {
    Write-Verbose "Error checking bin directory: $($_.Exception.Message)"
}

# Summary
Write-Host "" 
Write-Host "=== Cleanup Summary ===" -ForegroundColor Cyan

if ($totalProcesses -eq 0) {
    Write-Host "✓ No voice processes were running." -ForegroundColor Green
}
else {
    Write-Host "✓ Found and processed $totalProcesses voice-related processes." -ForegroundColor Green
    
    if ($killedProcesses.Count -gt 0) {
        Write-Host "✓ Successfully killed $($killedProcesses.Count) processes:" -ForegroundColor Green
        foreach ($killed in $killedProcesses) {
            Write-Host "    - $killed" -ForegroundColor Green
        }
    }
    
    $failedKills = $totalProcesses - $killedProcesses.Count
    if ($failedKills -gt 0) {
        Write-Host "⚠ Failed to kill $failedKills processes." -ForegroundColor Orange
    }
}

Write-Host "✓ Process cleanup completed successfully." -ForegroundColor Green
Write-Host ""
