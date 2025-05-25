# GPT Voice Assistant

This is a voice-enabled GPT assistant that uses the OpenAI Realtime Conversation API to provide voice interaction with GPT models. The assistant can process voice input, generate voice output, and use plugins to provide additional functionality.

## Prerequisites

- .NET 9.0 or later
- OpenAI API key
- SoX audio tool

## Setup

1. Ensure you have the necessary .NET SDK installed
2. Set up your OpenAI API key in `MyAppSettings.json` file
3. Install the SoX audio tool:

### Installing SoX

#### Windows
- Run the included batch file: `install_sox_windows.bat` (simple option)
- Or run the PowerShell script: `.\setup_sox.ps1` (requires administrator privileges)
- Alternatively, install manually:
  - Using Chocolatey: `choco install sox.portable`
  - Download from [SoX SourceForge](https://sourceforge.net/projects/sox/)

#### macOS
- Using Homebrew: `brew install sox`

#### Linux
- Using apt: `sudo apt install sox`
- Using yum: `sudo yum install sox`

**Note**: If SoX is not installed or accessible, the application will automatically run in text mode instead of voice mode.

## Running the Application

1. Build the project:
   ```
   dotnet build
   ```

2. Run the application:
   ```
   dotnet run
   ```

3. Follow the prompts:
   - Press Enter to start recording
   - Speak your query
   - Press Enter again to stop recording

## Features

- Voice-to-text transcription using Whisper model
- Text-to-speech synthesis with Echo voice
- Integration with Semantic Kernel plugins
- Automatic handling of audio recording and playback
- Fallback to text mode if voice capabilities are not available

## Troubleshooting

### SoX Not Found
- Ensure SoX is installed and in your PATH
- For Windows:
  - Run the `install_sox_windows.bat` file to install SoX automatically
  - Or run the `setup_sox.ps1` PowerShell script
- After installation, restart your terminal or PowerShell session
- The application will automatically fall back to text mode if SoX is not available

### Audio File Access Issues
- If you get "file is being used by another process" errors:
  - Close any applications that might be using your audio files
  - Restart the application
  - Delete any temporary audio files in the application directory
  - Try increasing the delay between recording steps

### Audio Recording Issues
- Ensure your microphone is properly connected and set as the default recording device
- Check microphone permissions for your terminal/console application
- Try using the Windows Sound settings to test your microphone
- If recording fails, the application will automatically switch to text mode

### API Errors
- Verify your API key in `MyAppSettings.json` is correct and has access to the required models
- Check internet connectivity
- Ensure you have access to the GPT-4o-realtime model in your OpenAI account

## License

This project is licensed under the MIT License - see the LICENSE file for details.
