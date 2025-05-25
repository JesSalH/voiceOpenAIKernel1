using System.Diagnostics;
#pragma warning disable OPENAI002
using OpenAI.RealtimeConversation;
#pragma warning restore OPENAI002
using Microsoft.Extensions.Logging;

namespace semanticKernelSample1.Assistant
{
    /// <summary>
    /// Voice recording and playback helper for the GPT Assistant
    /// </summary>
    public class VoiceHandler
    {
        private readonly ILogger _logger;
        private const string TEMP_AUDIO_FOLDER = "temp_audio";
        
        public VoiceHandler(ILogger logger)
        {
            _logger = logger;
            CreateAudioTempFolder();
        }
        
        /// <summary>
        /// Creates a temporary folder for audio files if it doesn't exist
        /// </summary>
        private void CreateAudioTempFolder()
        {
            try
            {
                if (!Directory.Exists(TEMP_AUDIO_FOLDER))
                {
                    Directory.CreateDirectory(TEMP_AUDIO_FOLDER);
                    _logger.LogInformation("Created temporary audio folder: {FolderPath}", TEMP_AUDIO_FOLDER);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating temporary audio folder");
                // Continue without temp folder - will use current directory as fallback
            }
        }
        
        /// <summary>
        /// Records audio from the user's microphone using sox
        /// </summary>
        /// <returns>Path to the recorded audio file</returns>        
        public async Task<string> RecordAudioAsync()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var inputFileName = $"input_{timestamp}.wav";
            var inputAudioPath = Path.Combine(TEMP_AUDIO_FOLDER, inputFileName);
            
            try
            {
                // Ensure the temp folder exists
                if (!Directory.Exists(TEMP_AUDIO_FOLDER))
                {
                    CreateAudioTempFolder();
                }
            }
            catch
            {
                // If temp folder creation fails, fall back to working directory
                inputAudioPath = inputFileName;
                _logger.LogWarning("Using working directory for audio files as fallback");
            }
            
            var soxProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sox",
                    Arguments = $"-d {inputAudioPath}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                }
            };
            
            try
            {
                soxProcess.Start();
                
                Console.WriteLine("Recording... Press Enter to stop.");
                Console.ReadLine();
                
                soxProcess.Kill();
                await soxProcess.WaitForExitAsync();
                
                return inputAudioPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording audio with sox");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error recording audio: {ex.Message}");
                Console.ResetColor();
                throw;
            }
        }
        
        /// <summary>
        /// Converts the audio to the format expected by the OpenAI API
        /// </summary>
        /// <param name="inputPath">Path to the input audio file</param>
        /// <returns>Path to the converted audio file</returns>
        public async Task<string> ConvertAudioAsync(string inputPath)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var outputFileName = $"converted_{timestamp}.wav";
                var outputPath = Path.Combine(TEMP_AUDIO_FOLDER, outputFileName);
                
                // Try to use temp folder, fall back to default if needed
                if (!Directory.Exists(TEMP_AUDIO_FOLDER))
                {
                    outputPath = "inputaudio.wav";
                }
                
                var conversionProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sox",
                        Arguments = $"{inputPath} -b 16 -r 24000 -c 1 {outputPath}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Directory.GetCurrentDirectory()
                    }
                };
                
                Console.WriteLine("Converting audio to appropriate format...");
                conversionProcess.Start();
                await conversionProcess.WaitForExitAsync();
                
                // Ensure file handles are released
                await Task.Delay(200);
                
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting audio with sox");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error converting audio: {ex.Message}");
                Console.ResetColor();
                throw;
            }
        }
        
        /// <summary>
        /// Sends the audio file to the OpenAI API for processing
        /// </summary>
        /// <param name="session">The conversation session</param>
        /// <param name="audioPath">Path to the audio file</param>
        #pragma warning disable OPENAI002
        public async Task SendAudioToSessionAsync(RealtimeConversationSession session, string audioPath)
        #pragma warning restore OPENAI002
        {
            try
            {
                if (!File.Exists(audioPath))
                {
                    throw new FileNotFoundException($"Audio file not found: {audioPath}");
                }
                
                using (Stream inputAudioStream = File.OpenRead(audioPath))
                {
                    Console.WriteLine("Sending audio to AI assistant...");
                    await session.SendInputAudioAsync(inputAudioStream);
                }
                
                Console.WriteLine("Processing your request...");
                await session.StartResponseAsync();
                
                Console.WriteLine("Waiting for assistant response...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending audio to AI");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error sending audio: {ex.Message}");
                Console.ResetColor();
                throw;
            }
        }
        
        /// <summary>
        /// Checks if sox is available on the system
        /// </summary>
        public bool IsSoxAvailable()
        {
            try
            {
                // First, try a direct approach - just execute sox with version flag
                var directProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sox",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                try
                {
                    directProcess.Start();
                    directProcess.WaitForExit(1000); // Wait up to 1 second
                    return directProcess.ExitCode == 0;
                }
                catch
                {
                    // Sox not directly accessible, continue with path check
                }
                
                // If direct check fails, try using where/which command
                string fileName = "where";
                string arguments = "sox";
                
                if (!OperatingSystem.IsWindows())
                {
                    fileName = "which";
                    arguments = "sox";
                }
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true 
                    }
                };
                
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                return !string.IsNullOrEmpty(output);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Performs a complete audio recording and processing workflow
        /// </summary>
        #pragma warning disable OPENAI002
        public async Task RecordAndProcessAudioAsync(RealtimeConversationSession session)
        #pragma warning restore OPENAI002
        {
            try
            {
                // Record audio from microphone
                var inputAudioPath = await RecordAudioAsync();
                
                // Convert audio to proper format
                var convertedAudioPath = await ConvertAudioAsync(inputAudioPath);
                
                // Send audio to OpenAI for processing
                await SendAudioToSessionAsync(session, convertedAudioPath);
                
                // Clean up the original file
                TryDeleteFile(inputAudioPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in audio recording and processing workflow");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }
        }
          /// <summary>
        /// Attempts to delete a file, logging warnings on failure but not throwing exceptions
        /// </summary>
        /// <returns>True if the file was deleted successfully, false otherwise</returns>
        private bool TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                // Log but continue - this is not critical
                _logger.LogWarning(ex, "Could not delete temporary audio file {FilePath}", filePath);
                return false;
            }
        }        /// <summary>
        /// Cleans up all temporary audio files from various locations
        /// </summary>
        public void CleanupTempFiles()
        {
            try
            {
                var deletedCount = 0;
                
                // Define all locations to clean
                var cleanupLocations = new[]
                {
                    TEMP_AUDIO_FOLDER,
                    ".",
                    "bin\\Debug\\net9.0",
                    "bin\\Release\\net9.0",
                    "bin\\Debug",
                    "bin\\Release"
                };
                
                // Define patterns to match
                var patterns = new[] { "input_*.wav", "output*.wav", "converted_*.wav", "inputaudio.wav" };
                
                foreach (var location in cleanupLocations.Where(Directory.Exists))
                {
                    deletedCount += CleanupDirectory(location, patterns);
                }
                
                _logger.LogInformation("Temporary audio files cleanup completed. Deleted {Count} files.", deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up temporary audio files");
            }
        }
        
        /// <summary>
        /// Cleans up audio files in a specific directory using the given patterns
        /// </summary>
        private int CleanupDirectory(string directory, string[] patterns)
        {
            var deletedCount = 0;
            
            foreach (var pattern in patterns)
            {
                try
                {
                    var files = Directory.GetFiles(directory, pattern);
                    deletedCount += files.Count(TryDeleteFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error cleaning files with pattern {Pattern} in {Directory}", pattern, directory);
                }
            }
            
            return deletedCount;
        }
    }
}