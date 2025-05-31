# Voice Assistant Process Management Scripts

This directory contains several scripts to manage voice assistant processes and prevent build conflicts.

## Available Scripts

### 1. `quick_start.bat` (RECOMMENDED)
The simplest and most reliable way to start the voice assistant.
- Kills any existing voice processes
- Builds and runs the application 
- Cleans up processes after exit

**Usage:**
```batch
quick_start.bat
```

### 2. `start_voice_assistant_enhanced.bat`
Enhanced startup script with comprehensive logging and error handling.
- Comprehensive process cleanup
- Clean build (removes bin/obj folders)
- Detailed progress reporting
- Retry logic for failed builds

**Usage:**
```batch
start_voice_assistant_enhanced.bat
```

### 3. `start_voice_assistant.bat`
Basic startup script that builds then runs the application.

**Usage:**
```batch
start_voice_assistant.bat
```

## Cleanup Scripts

### 1. `kill_voice_processes_simple.bat` (RECOMMENDED)
Simple batch script using Windows `taskkill` command.
- Kills voiceSample1.exe processes
- Kills semanticKernelSample1.exe processes  
- Finds and kills dotnet processes running voice apps
- Works reliably on all Windows systems

**Usage:**
```batch
kill_voice_processes_simple.bat
```

### 2. `kill_voice_processes.ps1`
Basic PowerShell cleanup script.
- Requires PowerShell execution policy to allow scripts
- More detailed process detection

**Usage:**
```powershell
powershell -ExecutionPolicy Bypass -File kill_voice_processes.ps1
```

### 3. `cleanup_voice_processes.ps1`
Advanced PowerShell script with comprehensive cleanup and logging.
- Verbose output and debugging options
- Graceful process shutdown before force kill
- Checks for locked files in bin directory
- Detailed summary reporting

**Usage:**
```powershell
powershell -ExecutionPolicy Bypass -File cleanup_voice_processes.ps1 -Verbose
```

## Common Issues and Solutions

### Build Error: "file is locked by another process"
This happens when the application is still running from a previous session.

**Solution:** Run any of the cleanup scripts before building:
```batch
kill_voice_processes_simple.bat
dotnet build
```

### PowerShell Execution Policy Error
If you get "execution policy" errors with PowerShell scripts:

**Solution:** Use the batch scripts instead, or run PowerShell as Administrator and set:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Application Won't Start After Cleanup
If the application doesn't start after cleanup:

**Solution:** Try a clean build:
```batch
rmdir /s /q bin obj
dotnet build
dotnet run
```

## Recommended Workflow

1. **For daily development:** Use `quick_start.bat`
2. **For troubleshooting:** Use `start_voice_assistant_enhanced.bat`
3. **For manual cleanup:** Use `kill_voice_processes_simple.bat`

## Notes

- All scripts are designed to be run from the project root directory
- The scripts will automatically detect and kill processes running your voice application
- It's safe to run the cleanup scripts even when no processes are running
- The enhanced scripts provide more detailed logging for debugging purposes
