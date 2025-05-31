# PowerShell script to kill all voice-related processes before starting the application
# This prevents build errors when the executable is locked by running processes

Write-Host "Checking for running voice processes..." -ForegroundColor Yellow

# List of process names to kill (without .exe extension)
$processesToKill = @(
    "voiceSample1",
    "semanticKernelSample1",
    "dotnet"  # Kill any dotnet processes that might be running our app
)

$killedProcesses = @()

foreach ($processName in $processesToKill) {
    try {
        $processes = Get-Process -Name $processName -ErrorAction SilentlyContinue
        
        if ($processes) {
            foreach ($process in $processes) {
                # For dotnet processes, check if they're running our specific application
                if ($processName -eq "dotnet") {
                    $commandLine = ""
                    try {
                        $commandLine = (Get-WmiObject Win32_Process -Filter "ProcessId = $($process.Id)").CommandLine
                    }
                    catch {
                        # If we can't get command line, skip this process
                        continue
                    }
                    
                    # Only kill dotnet processes that are running our voice application
                    if ($commandLine -and ($commandLine -like "*voiceSample1*" -or $commandLine -like "*semanticKernelSample1*")) {
                        Write-Host "Killing dotnet process (PID: $($process.Id)) running voice app..." -ForegroundColor Red
                        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                        $killedProcesses += "$processName (PID: $($process.Id))"
                    }
                }
                else {
                    Write-Host "Killing process: $processName (PID: $($process.Id))" -ForegroundColor Red
                    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                    $killedProcesses += "$processName (PID: $($process.Id))"
                }
            }
        }
    }
    catch {
        # Process might not exist or already be killed - continue silently
    }
}

# Wait a moment for processes to fully terminate
Start-Sleep -Milliseconds 500

if ($killedProcesses.Count -gt 0) {
    Write-Host "Killed processes:" -ForegroundColor Green
    foreach ($killed in $killedProcesses) {
        Write-Host "  - $killed" -ForegroundColor Green
    }
}
else {
    Write-Host "No voice processes found running." -ForegroundColor Green
}

Write-Host "Process cleanup complete." -ForegroundColor Green
