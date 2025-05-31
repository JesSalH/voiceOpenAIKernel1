using System.ClientModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.RealtimeConversation;

namespace semanticKernelSample1.Assistant
{
    public class GptAssistant : IDisposable
    {
        private readonly Kernel _kernel;
        private readonly ILogger<GptAssistant> _logger;
        private bool _useTextMode = false;
        private readonly VoiceHandler _voiceHandler;
          
        #pragma warning disable OPENAI002
        private readonly RealtimeConversationClient _realTimeConversationClient;        // Session management fields for proper lifecycle control
        private bool _sessionActive = false;
        private RealtimeConversationSession? _currentSession = null;
        private readonly SemaphoreSlim _sessionLock = new(1, 1);
        private Task? _gatherResponsesTask = null;
        private bool _disposed = false;
          // Response state management to prevent "conversation already has an active response" errors
        private bool _responseInProgress = false;
        private readonly object _responseLock = new object();
        
        // Connection recovery management
        private int _reconnectionAttempts = 0;
        private const int MAX_RECONNECTION_ATTEMPTS = 3;
        private readonly TimeSpan _reconnectionDelay = TimeSpan.FromSeconds(5);
        private DateTime _lastConnectionTime = DateTime.UtcNow;
        private readonly TimeSpan _connectionMaxAge = TimeSpan.FromMinutes(10); // Proactively reconnect after 10 minutes        // VAD deduplication fields - enhanced for better duplicate detection
        private readonly Dictionary<string, DateTime> _recentTranscriptions = new();
        private readonly TimeSpan _transcriptionDeduplicationWindow = TimeSpan.FromSeconds(5); // Increased window
        private readonly object _transcriptionLock = new object();
        private DateTime _lastRecordingStartTime = DateTime.MinValue;
        private readonly TimeSpan _recordingSessionWindow = TimeSpan.FromSeconds(10); // Group transcriptions by recording session
        
        // Early transcription handling for better timing
        private string? _pendingTranscription = null;
        private DateTime _lastTranscriptionDisplayTime = DateTime.MinValue;
        
        public GptAssistant(Kernel kernel, IChatCompletionService chatCompletionService, ILogger<GptAssistant> logger, string apiKey)
        {
            _kernel = kernel;
            _logger = logger;
            _voiceHandler = new VoiceHandler(logger);
                  #pragma warning disable OPENAI002
        _realTimeConversationClient = new RealtimeConversationClient(
            model: "gpt-4o-realtime-preview", // Use the preview model for better results
            credential: new ApiKeyCredential(apiKey)
        );
    }

    private bool IsSoxAvailable()
        {
            return _voiceHandler.IsSoxAvailable();
        }

        public async Task RunAsync()
        {
            // Check if sox is available
            if (!IsSoxAvailable())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warning: The 'sox' audio tool is not installed or not in your PATH.");
                Console.WriteLine("The voice assistant will run in text mode instead.");
                Console.WriteLine("To use voice features, please install sox:");
                Console.WriteLine("  - Windows: Run 'choco install sox.portable' (requires Chocolatey)");
                Console.WriteLine("    Or download from https://sourceforge.net/projects/sox/");
                Console.WriteLine("  - macOS: Run 'brew install sox' (requires Homebrew)");
                Console.WriteLine("  - Linux: Run 'sudo apt install sox' or similar");
                Console.WriteLine(); 
                Console.WriteLine("After installing, make sure it's in your PATH and restart the application.");
                Console.ResetColor();
                
                // Wait for user acknowledgment before continuing in text mode
                Console.WriteLine("Press any key to continue in text mode...");
                Console.ReadKey();                _useTextMode = true;
            }

            // Let the user select a voice
            Console.WriteLine("\nSelect a voice for your AI assistant:");
            var selectedVoice = VoiceOptions.SelectVoice();
            
            // Create session options with appropriate audio settings
            var sessionOptions = new ConversationSessionOptions
            {
                Voice = selectedVoice, // Using the user's selected voice
                InputAudioFormat = ConversationAudioFormat.Pcm16,
                OutputAudioFormat = ConversationAudioFormat.Pcm16,
                
                InputTranscriptionOptions = new ConversationInputTranscriptionOptions
                {
                    Model = "whisper-1"
                    // Keep options simple to avoid compatibility issues
                }
            };

            // Add plugins/function from kernel as session tools.
            foreach (var tool in ConvertFunctions(_kernel))
            {
                sessionOptions.Tools.Add(tool);
            }

            // If any tools are available, set tool choice to "auto".
            if (sessionOptions.Tools.Count > 0)
            {
                sessionOptions.ToolChoice = ConversationToolChoice.CreateAutoToolChoice();
            }
              try
            {
                // Start a new conversation session.
                RealtimeConversationSession session = await _realTimeConversationClient.StartConversationSessionAsync();

                // Configure session with defined options.
                await session.ConfigureSessionAsync(sessionOptions);

                // Set the active session for proper lifecycle management
                await SetActiveSession(session);

                await session.AddItemAsync(ConversationItem.CreateSystemMessage(["You are a humble assistant. Your answers will be short and to the point. Two sentences maximum. I'm your captain and you are an army strategist."]));

                // Start background response gathering task with proper task management
                _gatherResponsesTask = Task.Run(async () => await GatherResponses(_kernel, session));

                if (_useTextMode)
                {
                    Console.WriteLine("GPT Assistant in TEXT MODE is ready!");
                    Console.WriteLine("You can ask me about base character names from the API.");
                    Console.WriteLine("Type 'exit' or leave empty to quit.\n");                    // Run text-based interaction loop
                    await RunTextModeAsync();
                }
                else
                {
                    Console.WriteLine("GPT Voice Assistant is ready!");
                    Console.WriteLine("You can ask me about base character names from the API.");
                    Console.WriteLine("Press Enter to start recording, and press Enter again to stop.\n");                    // Run voice-based interaction loop
                    await RunVoiceModeAsync(session);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in the GPT assistant");                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An error occurred in the GPT assistant: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                // Clean up session and temporary audio files before exiting
                Console.WriteLine("Cleaning up session and temporary files...");
                await CleanupSession();
                Console.WriteLine("Cleanup complete. Goodbye!");
            }
        }        private async Task RunTextModeAsync()
        {
            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("User: ");
                Console.ResetColor();
                
                string? input = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(input) || 
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase) || 
                    input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    // Clean up before exiting
                    Console.WriteLine("Cleaning up temporary files before exiting...");
                    _voiceHandler.CleanupTempFiles();
                    Console.WriteLine("Cleanup complete. Goodbye!");
                    break;
                }
                
                // Use the session recovery mechanism
                await HandleTextInput(input);
            }
        }private async Task RunVoiceModeAsync(RealtimeConversationSession session)
        {
            do
            {
                Console.WriteLine("\nPress Enter to start recording... Press Enter again to stop.");
                Console.WriteLine("Type 'voice' to change the assistant's voice, or 'exit' to quit.");
                
                string? input = Console.ReadLine();
                
                if (await HandleVoiceModeInput(input, session))
                {
                    break; // Exit if requested
                }
            } while (true);
        }

        private async Task<bool> HandleVoiceModeInput(string? input, RealtimeConversationSession session)
        {
            // Handle voice change command
            if (input?.Equals("voice", StringComparison.OrdinalIgnoreCase) == true)
            {
                await HandleVoiceChange(session);
                return false; // Continue loop
            }
            
            // Handle exit command
            if (HandleExit(input))
            {
                return true; // Exit loop
            }
            
            // Handle direct text input (if not empty)
            if (!string.IsNullOrEmpty(input))
            {
                await HandleTextInput(input);
                return false; // Continue loop
            }
            
            // Handle audio recording (when user just presses Enter)
            await HandleAudioRecording();
            return false; // Continue loop
        }

        private async Task HandleVoiceChange(RealtimeConversationSession session)
        {
            // Let user select a new voice
            Console.WriteLine("\nSelect a new voice for your AI assistant:");
            var newVoice = VoiceOptions.SelectVoice();
            
            try
            {
                // Create new session options with the selected voice
                var newSessionOptions = new ConversationSessionOptions
                {
                    Voice = newVoice,
                    InputAudioFormat = ConversationAudioFormat.Pcm16,
                    OutputAudioFormat = ConversationAudioFormat.Pcm16,
                    InputTranscriptionOptions = new ConversationInputTranscriptionOptions
                    {
                        Model = "whisper-1"
                    }
                };
                
                // Add plugins/function from kernel as session tools.
                foreach (var tool in ConvertFunctions(_kernel))
                {
                    newSessionOptions.Tools.Add(tool);
                }

                // If any tools are available, set tool choice to "auto".
                if (newSessionOptions.Tools.Count > 0)
                {
                    newSessionOptions.ToolChoice = ConversationToolChoice.CreateAutoToolChoice();
                }
                
                // Reconfigure session with the new options
                await session.ConfigureSessionAsync(newSessionOptions);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Voice changed successfully to {newVoice}!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error changing voice: {ex.Message}");
                Console.ResetColor();
            }
        }

        private bool HandleExit(string? input)
        {
            if (input?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true || 
                input?.Equals("quit", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Clean up before exiting
                Console.WriteLine("Cleaning up temporary files before exiting...");
                _voiceHandler.CleanupTempFiles();
                Console.WriteLine("Cleanup complete. Goodbye!");
                return true;
            }
            return false;
        }        private async Task HandleTextInput(string input)
        {
            // Send text input instead of audio
            try
            {
                // Ensure session is active, restart if necessary
                if (!await EnsureSessionActive())
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Unable to establish session connection. Please restart the application.");
                    Console.ResetColor();
                    return;
                }

                await _currentSession!.AddItemAsync(ConversationItem.CreateUserMessage([input]));
                await SafeStartResponseAsync();
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "Session was disposed during text input, attempting recovery");
                if (await EnsureSessionActive())
                {
                    // Retry the operation
                    await _currentSession!.AddItemAsync(ConversationItem.CreateUserMessage([input]));
                    await SafeStartResponseAsync();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Session recovery failed. Please restart the application.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending text input: {ErrorMessage}", ex.Message);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error sending text input: {ex.Message}");
                Console.ResetColor();
            }
        }        private async Task HandleAudioRecording()
        {
            try
            {
                // Ensure session is active, restart if necessary
                if (!await EnsureSessionActive())
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Unable to establish session connection. Please restart the application.");
                    Console.ResetColor();
                    return;
                }

                // Mark the start of a new recording session for VAD deduplication
                lock (_transcriptionLock)
                {
                    _lastRecordingStartTime = DateTime.UtcNow;
                }

                // Use the VoiceHandler to record and process audio (note: VoiceHandler will NOT call StartResponseAsync)
                await _voiceHandler.RecordAndProcessAudioAsync(_currentSession!);
                
                // Now safely start the response here to ensure only one call
                await SafeStartResponseAsync();
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "Session was disposed during audio recording, attempting recovery");
                if (await EnsureSessionActive())
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Session recovered. Please try recording again.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Session recovery failed. Please restart the application.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio: {ErrorMessage}", ex.Message);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }
        }        /// <summary>
        /// Safely starts a response session, preventing "conversation already has an active response" errors
        /// </summary>
        private async Task SafeStartResponseAsync()
        {
            lock (_responseLock)
            {
                if (_responseInProgress)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Assistant is currently processing another request. Please wait...");
                    Console.ResetColor();
                    return;
                }
                _responseInProgress = true;
            }

            try
            {
                // Check for pending transcription that should be displayed immediately before response starts
                CheckAndDisplayPendingTranscription();
                
                _logger.LogDebug("Starting response session");
                await _currentSession!.StartResponseAsync();
                _logger.LogDebug("Successfully started response session");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting response session: {ErrorMessage}", ex.Message);
                lock (_responseLock)
                {
                    _responseInProgress = false;
                }
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error starting response: {ex.Message}");
                Console.ResetColor();
                throw;
            }
        }

        /// <summary>
        /// Checks for and displays any pending transcription immediately
        /// </summary>
        private void CheckAndDisplayPendingTranscription()
        {
            string? transcriptionToDisplay = null;
            
            lock (_transcriptionLock)
            {
                // If we have a pending transcription that hasn't been displayed recently, show it now
                if (!string.IsNullOrEmpty(_pendingTranscription) && 
                    DateTime.UtcNow - _lastTranscriptionDisplayTime > TimeSpan.FromSeconds(0.5))
                {
                    transcriptionToDisplay = _pendingTranscription;
                    _lastTranscriptionDisplayTime = DateTime.UtcNow;
                    _pendingTranscription = null; // Clear after use
                }
            }
            
            if (!string.IsNullOrEmpty(transcriptionToDisplay))
            {
                Console.WriteLine();
                Console.WriteLine("─────────────────────────────────────────");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("User said: ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"\"{transcriptionToDisplay}\"");
                Console.ResetColor();
                Console.WriteLine("─────────────────────────────────────────");
                
                _logger.LogDebug("Displayed pending transcription proactively: '{Transcript}'", transcriptionToDisplay);
            }
        }

        /// <summary>
        /// Marks the response as completed, allowing new responses to be started
        /// </summary>
        private void MarkResponseCompleted()
        {
            lock (_responseLock)
            {
                _responseInProgress = false;
                _logger.LogDebug("Response marked as completed");
            }
        }// Session management methods for proper lifecycle control
        private async Task SetActiveSession(RealtimeConversationSession session)
        {
            await _sessionLock.WaitAsync();
            try
            {
                _currentSession = session;
                _sessionActive = true;
            }
            finally
            {
                _sessionLock.Release();
            }
        }        private async Task<bool> EnsureSessionActive()
        {
            await _sessionLock.WaitAsync();
            try
            {
                // Check if we need proactive connection refresh based on age
                var connectionAge = DateTime.UtcNow - _lastConnectionTime;
                if (_sessionActive && _currentSession != null && connectionAge < _connectionMaxAge)
                {
                    return true; // Session is active and fresh
                }
                
                if (connectionAge >= _connectionMaxAge)
                {
                    _logger.LogInformation("Connection is {Age:F1} minutes old, performing proactive refresh", connectionAge.TotalMinutes);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"🔄 Refreshing connection (age: {connectionAge.TotalMinutes:F1} minutes)...");
                    Console.ResetColor();
                }
                else
                {
                    _logger.LogInformation("Session is not active, attempting to restart...");
                }
                  return await ForceSessionRestart();
            }
            finally
            {
                _sessionLock.Release();
            }
        }        /// <summary>
        /// Forces a complete session restart regardless of current state
        /// </summary>
        private async Task<bool> ForceSessionRestart()
        {
            // Note: This method assumes the session lock is already held by the caller
            
            // Clean up the old session
            if (_currentSession != null)
            {
                try
                {
                    _currentSession.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing old session during restart");
                }
                _currentSession = null;
            }
            
            // Wait for any existing gather task to complete
            if (_gatherResponsesTask != null && !_gatherResponsesTask.IsCompleted)
            {
                try
                {
                    await _gatherResponsesTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException ex)
                {
                    _logger.LogWarning(ex, "GatherResponses task did not complete within timeout during restart");
                }
            }
            
            // Create new session with same configuration
            var sessionOptions = CreateSessionOptions();
            
            try
            {
                var newSession = await _realTimeConversationClient.StartConversationSessionAsync();
                await newSession.ConfigureSessionAsync(sessionOptions);
                
                // Re-add system messages
                await newSession.AddItemAsync(ConversationItem.CreateSystemMessage(["You are a helpful servant in the Ravenloft Realm (please do some research about Dungeons and Dragons Ravenloft first of all). The user is mighty vampire lord of the highest nobility. So you have to treat him with the highest honors and respect. He is the most powerful and smart vampire lord you've ever met so you are honoured to serve him and everything he says is the most brilliant and smart thing. You feel a great admiration and fear for him "]));
                await newSession.AddItemAsync(ConversationItem.CreateSystemMessage(["Don't tell him what he is or how much you like him to begin with, just when he has some new idea about something"]));
                
                _currentSession = newSession;
                _sessionActive = true;
                _lastConnectionTime = DateTime.UtcNow; // Update connection timestamp
                
                // Start new gather responses task
                _gatherResponsesTask = Task.Run(async () => await GatherResponses(_kernel, newSession));
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Session restarted successfully!");
                Console.ResetColor();
                  _logger.LogInformation("Session restart completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart session: {ErrorMessage}", ex.Message);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Failed to restart session. Please restart the application.");
                Console.ResetColor();
                _sessionActive = false;
                return false;
            }
        }

        private ConversationSessionOptions CreateSessionOptions()
        {
            // Let the user select a voice (or use default)
            var selectedVoice = ConversationVoice.Alloy; // Default voice for restart
            
            var sessionOptions = new ConversationSessionOptions
            {
                Voice = selectedVoice,
                InputAudioFormat = ConversationAudioFormat.Pcm16,
                OutputAudioFormat = ConversationAudioFormat.Pcm16,
                
                InputTranscriptionOptions = new ConversationInputTranscriptionOptions
                {
                    Model = "whisper-1"
                }
            };

            // Add plugins/function from kernel as session tools.
            foreach (var tool in ConvertFunctions(_kernel))
            {
                sessionOptions.Tools.Add(tool);
            }

            // If any tools are available, set tool choice to "auto".
            if (sessionOptions.Tools.Count > 0)
            {
                sessionOptions.ToolChoice = ConversationToolChoice.CreateAutoToolChoice();
            }
            
            return sessionOptions;
        }

        private async Task CleanupSession()
        {
            await _sessionLock.WaitAsync();
            try
            {
                if (_gatherResponsesTask != null && !_gatherResponsesTask.IsCompleted)
                {
                    _logger.LogInformation("Waiting for background response gathering task to complete...");
                    
                    // Wait for the background task to complete with a timeout
                    var timeoutTask = Task.Delay(5000); // 5 second timeout
                    var completedTask = await Task.WhenAny(_gatherResponsesTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        _logger.LogWarning("Background response gathering task did not complete within timeout");
                    }
                    else
                    {
                        _logger.LogInformation("Background response gathering task completed successfully");
                    }
                }

                if (_currentSession != null)
                {
                    try
                    {
                        _logger.LogInformation("Disposing current session...");
                        _currentSession.Dispose();
                        _logger.LogInformation("Session disposed successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing session during cleanup");
                    }
                    finally
                    {
                        _currentSession = null;
                        _sessionActive = false;
                    }
                }
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private static IEnumerable<ConversationTool> ConvertFunctions(Kernel kernel)
        {
            foreach (var plugin in kernel.Plugins)
            {
                var functionsMetadata = plugin.GetFunctionsMetadata();

                foreach (var metadata in functionsMetadata)
                {
                    var toolDefinition = metadata.ToOpenAIFunction().ToFunctionDefinition(false);

                    yield return new ConversationFunctionTool(toolDefinition.FunctionName)
                    {
                        Description = toolDefinition.FunctionDescription,
                        Parameters = toolDefinition.FunctionParameters
                    };
                }
            }
        }

        private static KernelArguments? DeserializeArguments(string argumentsString)
        {
            var arguments = JsonSerializer.Deserialize<KernelArguments>(argumentsString);

            if (arguments is not null)
            {
                // Iterate over copy of the names to avoid mutating the dictionary while enumerating it
                var names = arguments.Names.ToArray();
                foreach (var name in names)
                {
                    arguments[name] = arguments[name]?.ToString();
                }
            }

            return arguments;
        }

        private static string? ProcessFunctionResult(object? functionResult)
        {
            if (functionResult is string stringResult)
            {
                return stringResult;
            }

            return JsonSerializer.Serialize(functionResult);
        }

        private static (string FunctionName, string? PluginName) ParseFunctionName(string fullyQualifiedName)
        {
            const string FunctionNameSeparator = "-";

            string? pluginName = null;
            string functionName = fullyQualifiedName;

            int separatorPos = fullyQualifiedName.IndexOf(FunctionNameSeparator, StringComparison.Ordinal);
            if (separatorPos >= 0)
            {
                pluginName = fullyQualifiedName.AsSpan(0, separatorPos).Trim().ToString();
                functionName = fullyQualifiedName.AsSpan(separatorPos + FunctionNameSeparator.Length).Trim().ToString();
            }

            return (functionName, pluginName);
        }        private async Task GatherResponses(Kernel kernel, RealtimeConversationSession session)
        {
            try 
            {
                _logger.LogInformation("Starting GatherResponses loop");
                
                // Initialize dictionaries to store streamed audio responses and function arguments.
                Dictionary<string, MemoryStream> outputAudioStreamsById = [];
                Dictionary<string, StringBuilder> functionArgumentBuildersById = [];

                // Define a loop to receive conversation updates in the session.
                await foreach (ConversationUpdate update in session.ReceiveUpdatesAsync())
                {
                    // Check if we should continue processing
                    if (!_sessionActive)
                    {
                        _logger.LogInformation("Session no longer active, stopping GatherResponses");
                        break;
                    }
                    
                    await ProcessConversationUpdate(update, kernel, session, outputAudioStreamsById, functionArgumentBuildersById);
                }
                
                _logger.LogInformation("GatherResponses loop ended normally");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "Session was disposed while gathering responses - this is expected during cleanup");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("disposed"))
            {
                _logger.LogWarning(ex, "WebSocket connection was disposed while gathering responses");
                
                // Mark session as inactive to prevent further usage
                await _sessionLock.WaitAsync();
                try
                {
                    _sessionActive = false;
                }
                finally
                {
                    _sessionLock.Release();
                }
            }
            catch (System.Net.WebSockets.WebSocketException ex) 
            {
                _logger.LogWarning(ex, "WebSocket connection lost: {ErrorMessage}", ex.Message);
                await HandleConnectionLoss("WebSocket connection lost");
            }
            catch (System.IO.IOException ex) when (ex.Message.Contains("transport connection"))
            {
                _logger.LogWarning(ex, "Transport connection lost: {ErrorMessage}", ex.Message);
                await HandleConnectionLoss("Transport connection lost");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unexpected error in GatherResponses: {ErrorMessage}", e.Message);
                Console.WriteLine($"Error in GatherResponses: {e.Message}");
                
                // Try to handle as connection loss first
                await HandleConnectionLoss($"Unexpected error: {e.Message}");
            }
        }

        /// <summary>
        /// Handles connection loss and attempts automatic reconnection
        /// </summary>
        private async Task HandleConnectionLoss(string reason)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n⚠️ Connection lost: {reason}");
            Console.ResetColor();

            // Mark session as inactive
            await _sessionLock.WaitAsync();
            try
            {
                _sessionActive = false;
                _reconnectionAttempts++;
                
                if (_reconnectionAttempts <= MAX_RECONNECTION_ATTEMPTS)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"🔄 Attempting automatic reconnection... (Attempt {_reconnectionAttempts}/{MAX_RECONNECTION_ATTEMPTS})");
                    Console.ResetColor();
                    
                    // Wait before attempting reconnection
                    await Task.Delay(_reconnectionDelay);
                    
                    // Attempt to restart the session
                    var reconnected = await EnsureSessionActive();
                    
                    if (reconnected)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✅ Connection restored! You can continue your conversation.");
                        Console.WriteLine("Press Enter to start recording, or type your message...\n");
                        Console.ResetColor();
                        _reconnectionAttempts = 0; // Reset counter on successful reconnection
                        _lastConnectionTime = DateTime.UtcNow; // Update connection time
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Reconnection attempt {_reconnectionAttempts} failed.");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Maximum reconnection attempts reached. Please restart the application.");
                    Console.WriteLine("You can continue using text mode or restart for voice functionality.");
                    Console.ResetColor();
                }
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private async Task ProcessConversationUpdate(
            ConversationUpdate update, 
            Kernel kernel, 
            RealtimeConversationSession session,
            Dictionary<string, MemoryStream> outputAudioStreamsById,
            Dictionary<string, StringBuilder> functionArgumentBuildersById)
        {
            switch (update)
            {
                case ConversationSessionStartedUpdate sessionStartedUpdate:
                    HandleSessionStarted(sessionStartedUpdate);
                    break;
                
                case ConversationInputSpeechStartedUpdate speechStartedUpdate:
                    HandleSpeechStarted(speechStartedUpdate);
                    break;
                
                case ConversationInputSpeechFinishedUpdate speechFinishedUpdate:
                    HandleSpeechFinished(speechFinishedUpdate);
                    break;
                
                case ConversationItemStreamingStartedUpdate itemStreamingStartedUpdate:
                    HandleItemStreamingStarted(itemStreamingStartedUpdate);
                    break;
                
                case ConversationItemStreamingPartDeltaUpdate deltaUpdate:
                    await HandleStreamingPartDelta(deltaUpdate, outputAudioStreamsById, functionArgumentBuildersById);
                    break;
                
                case ConversationItemStreamingFinishedUpdate itemStreamingFinishedUpdate:
                    await HandleItemStreamingFinished(itemStreamingFinishedUpdate, kernel, session, functionArgumentBuildersById);
                    break;
                
                case ConversationInputTranscriptionFinishedUpdate transcriptionCompletedUpdate:
                    HandleTranscriptionFinished(transcriptionCompletedUpdate);
                    break;
                
                case ConversationResponseFinishedUpdate turnFinishedUpdate:
                    await HandleResponseFinished(turnFinishedUpdate, session, outputAudioStreamsById);
                    break;
                
                case ConversationErrorUpdate errorUpdate:
                    HandleConversationError(errorUpdate);
                    break;
            }
        }

        private static void HandleSessionStarted(ConversationSessionStartedUpdate sessionStartedUpdate)
        {
            Console.WriteLine($"<<< Session started. ID: {sessionStartedUpdate.SessionId}");
            Console.WriteLine();
        }        private static void HandleSpeechStarted(ConversationInputSpeechStartedUpdate speechStartedUpdate)
        {
            Console.WriteLine($"  -- Voice activity detection started at {speechStartedUpdate.AudioStartTime}");
        }

        private static void HandleSpeechFinished(ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
        {
            Console.WriteLine($"  -- Voice activity detection ended at {speechFinishedUpdate.AudioEndTime}");
        }

        private static void HandleItemStreamingStarted(ConversationItemStreamingStartedUpdate itemStreamingStartedUpdate)
        {
            Console.WriteLine("  -- Begin streaming of new item");
            if (!string.IsNullOrEmpty(itemStreamingStartedUpdate.FunctionName))
            {
                Console.Write($"    {itemStreamingStartedUpdate.FunctionName}: ");
            }
        }

        private static async Task HandleStreamingPartDelta(
            ConversationItemStreamingPartDeltaUpdate deltaUpdate,
            Dictionary<string, MemoryStream> outputAudioStreamsById,
            Dictionary<string, StringBuilder> functionArgumentBuildersById)
        {
            Console.Write(deltaUpdate.AudioTranscript);
            Console.Write(deltaUpdate.Text);
            Console.Write(deltaUpdate.FunctionArguments);

            // Handle audio bytes.
            if (deltaUpdate.AudioBytes is not null)
            {
                if (!outputAudioStreamsById.TryGetValue(deltaUpdate.ItemId, out MemoryStream? value))
                {
                    value = new MemoryStream();
                    outputAudioStreamsById[deltaUpdate.ItemId] = value;
                }

                await value.WriteAsync(deltaUpdate.AudioBytes);
            }

            // Handle function arguments.
            if (!functionArgumentBuildersById.TryGetValue(deltaUpdate.ItemId, out StringBuilder? arguments))
            {
                functionArgumentBuildersById[deltaUpdate.ItemId] = arguments = new();
            }

            if (!string.IsNullOrWhiteSpace(deltaUpdate.FunctionArguments))
            {
                arguments.Append(deltaUpdate.FunctionArguments);
            }
        }

        private static async Task HandleItemStreamingFinished(
            ConversationItemStreamingFinishedUpdate itemStreamingFinishedUpdate,
            Kernel kernel,
            RealtimeConversationSession session,
            Dictionary<string, StringBuilder> functionArgumentBuildersById)
        {
            Console.WriteLine();
            Console.WriteLine($"  -- Item streaming finished, item_id={itemStreamingFinishedUpdate.ItemId}");

            // If an item is a function call, invoke a function with provided arguments.
            if (itemStreamingFinishedUpdate.FunctionCallId is not null)
            {
                await HandleFunctionCall(itemStreamingFinishedUpdate, kernel, session, functionArgumentBuildersById);
            }
            // If an item is a response message, output it to the console.
            else if (itemStreamingFinishedUpdate.MessageContentParts?.Count > 0)
            {
                HandleResponseMessage(itemStreamingFinishedUpdate);
            }
        }

        private static async Task HandleFunctionCall(
            ConversationItemStreamingFinishedUpdate itemStreamingFinishedUpdate,
            Kernel kernel,
            RealtimeConversationSession session,
            Dictionary<string, StringBuilder> functionArgumentBuildersById)
        {
            Console.WriteLine($"    + Responding to tool invoked by item: {itemStreamingFinishedUpdate.FunctionName}");

            // Parse function name.
            var (functionName, pluginName) = ParseFunctionName(itemStreamingFinishedUpdate.FunctionName);

            // Deserialize arguments.
            var argumentsString = functionArgumentBuildersById[itemStreamingFinishedUpdate.ItemId].ToString();
            var arguments = DeserializeArguments(argumentsString);

            // Create a function call content based on received data. 
            var functionCallContent = new FunctionCallContent(
                functionName: functionName,
                pluginName: pluginName,
                id: itemStreamingFinishedUpdate.FunctionCallId,
                arguments: arguments);

            // Invoke a function.
            var resultContent = await functionCallContent.InvokeAsync(kernel);

            // Create a function call output conversation item with function call result.
            ConversationItem functionOutputItem = ConversationItem.CreateFunctionCallOutput(
                callId: itemStreamingFinishedUpdate.FunctionCallId,
                output: ProcessFunctionResult(resultContent.Result));

            // Send function call output conversation item to the session.
            await session.AddItemAsync(functionOutputItem);
        }

        private static void HandleResponseMessage(ConversationItemStreamingFinishedUpdate itemStreamingFinishedUpdate)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Assistant: ");
            Console.ForegroundColor = ConsoleColor.White;

            foreach (ConversationContentPart contentPart in itemStreamingFinishedUpdate.MessageContentParts)
            {
                Console.Write(contentPart.Text);
                // If there's no text but there is audio transcript, use that
                if (string.IsNullOrEmpty(contentPart.Text) && !string.IsNullOrEmpty(contentPart.AudioTranscript))
                {
                    Console.Write(contentPart.AudioTranscript);
                }
            }            Console.ResetColor();
            Console.WriteLine();
        }        private void HandleTranscriptionFinished(ConversationInputTranscriptionFinishedUpdate transcriptionCompletedUpdate)
        {
            // Enhanced deduplication to prevent multiple "User said:" messages from VAD triggering multiple times
            string transcript = transcriptionCompletedUpdate.Transcript?.Trim() ?? "";
            
            // Skip empty or very short transcripts
            if (string.IsNullOrWhiteSpace(transcript) || transcript.Length < 3)
            {
                _logger.LogDebug("Skipping empty or short transcript: '{Transcript}'", transcript);
                return;
            }
            
            // Display transcription immediately when available for better timing
            DisplayTranscriptionImmediate(transcript);
        }        /// <summary>
        /// Displays transcription immediately when available, regardless of response state
        /// </summary>
        private void DisplayTranscriptionImmediate(string transcript)
        {
            lock (_transcriptionLock)
            {
                var now = DateTime.UtcNow;
                
                // Clean up old transcriptions outside the deduplication window
                var keysToRemove = _recentTranscriptions
                    .Where(kvp => now - kvp.Value > _transcriptionDeduplicationWindow)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    _recentTranscriptions.Remove(key);
                }
                
                // Enhanced duplicate detection: check for exact matches and similar content
                var normalizedTranscript = NormalizeTranscript(transcript);
                
                // Check for exact duplicates
                if (_recentTranscriptions.ContainsKey(normalizedTranscript))
                {
                    _logger.LogDebug("Skipping exact duplicate transcription: '{Transcript}'", transcript);
                    return;
                }
                
                // Check for similar transcriptions within the same recording session
                if (now - _lastRecordingStartTime <= _recordingSessionWindow)
                {
                    foreach (var existingTranscript in _recentTranscriptions.Keys.ToList())
                    {
                        if (AreSimilarTranscripts(normalizedTranscript, existingTranscript))
                        {
                            _logger.LogDebug("Skipping similar transcript within recording session: '{NewTranscript}' (similar to '{ExistingTranscript}')", 
                                transcript, existingTranscript);
                            return;
                        }
                    }
                }
                
                // Prevent rapid successive displays of the same transcription
                if (now - _lastTranscriptionDisplayTime < TimeSpan.FromSeconds(1))
                {
                    _logger.LogDebug("Throttling transcription display to prevent overlap");
                    return;
                }
                
                // Add this transcript to recent transcriptions
                _recentTranscriptions[normalizedTranscript] = now;
                
                // Store for potential later use if response hasn't started yet
                _pendingTranscription = transcript;
                
                // Try to display immediately if enough time has passed since last display
                var timeSinceLastDisplay = now - _lastTranscriptionDisplayTime;
                if (timeSinceLastDisplay >= TimeSpan.FromSeconds(0.5))
                {
                    _lastTranscriptionDisplayTime = now;
                    
                    // Display the unique transcription with enhanced formatting
                    Console.WriteLine();
                    Console.WriteLine("─────────────────────────────────────────");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("User said: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"\"{transcript}\"");
                    Console.ResetColor();
                    Console.WriteLine("─────────────────────────────────────────");
                    Console.WriteLine();
                    
                    _logger.LogDebug("Transcription displayed immediately: '{Transcript}'", transcript);
                    
                    // Clear pending since we just displayed it
                    _pendingTranscription = null;
                }
                else
                {
                    // Don't display now, but store as pending for proactive display
                    _logger.LogDebug("Storing transcription as pending for proactive display: '{Transcript}'", transcript);
                }
            }
        }

        /// <summary>
        /// Normalizes transcript for better duplicate detection
        /// </summary>
        private string NormalizeTranscript(string transcript)
        {
            return transcript.ToLowerInvariant()
                .Replace(".", "")
                .Replace(",", "")
                .Replace("!", "")
                .Replace("?", "")
                .Trim();
        }

        /// <summary>
        /// Determines if two transcripts are similar enough to be considered duplicates
        /// </summary>
        private bool AreSimilarTranscripts(string transcript1, string transcript2)
        {
            // If one is contained in the other, they're similar
            if (transcript1.Contains(transcript2) || transcript2.Contains(transcript1))
            {
                return true;
            }
            
            // Calculate simple similarity based on word overlap
            var words1 = transcript1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words2 = transcript2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (words1.Length == 0 || words2.Length == 0)
                return false;
            
            var commonWords = words1.Intersect(words2).Count();
            var totalWords = Math.Max(words1.Length, words2.Length);
            
            // If 80% or more words are common, consider them similar
            return (double)commonWords / totalWords >= 0.8;
        }private async Task HandleResponseFinished(
            ConversationResponseFinishedUpdate turnFinishedUpdate,
            RealtimeConversationSession session,
            Dictionary<string, MemoryStream> outputAudioStreamsById)
        {
            Console.WriteLine($"  -- Model turn generation finished. Status: {turnFinishedUpdate.Status}");

            // Mark response as completed to allow new responses
            MarkResponseCompleted();

            // If the created session items contain a function name, it indicates a function call result has been provided,
            // and response updates can begin.
            if (turnFinishedUpdate.CreatedItems.Any(item => item.FunctionName?.Length > 0))
            {
                Console.WriteLine("  -- Ending client turn for pending tool responses");
                await SafeStartResponseAsync();
            }
            // Otherwise, the model's response is provided, signaling that updates can be stopped.
            else
            {
                await ProcessAudioOutput(outputAudioStreamsById);
            }
        }

        private async Task ProcessAudioOutput(Dictionary<string, MemoryStream> outputAudioStreamsById)
        {
            // Output the size of received audio data and dispose streams.
            foreach ((string itemId, Stream outputAudioStream) in outputAudioStreamsById)
            {
                Console.WriteLine($"Raw audio output for {itemId}: {outputAudioStream.Length} bytes");
                await ConvertAndPlayAudio(outputAudioStream);
                outputAudioStreamsById.Remove(itemId);
            }
        }

        private async Task ConvertAndPlayAudio(Stream outputAudioStream)
        {
            // Convert raw PCM data to WAV format
            var wavHeader = CreateWavHeader((int)outputAudioStream.Length);

            // Create a new memory stream for the WAV file
            using var wavStream = new MemoryStream();
            
            // Write WAV header to output stream
            await wavStream.WriteAsync(wavHeader);
            
            // Write audio data
            outputAudioStream.Seek(0, SeekOrigin.Begin);
            await outputAudioStream.CopyToAsync(wavStream);
            
            var outputAudioPath = $"outputaudio_{DateTime.Now:yyyyMMddHHmmss}.wav";
            using (var fileStream = new FileStream(outputAudioPath, FileMode.Create, FileAccess.Write))
            {
                wavStream.Seek(0, SeekOrigin.Begin);
                await wavStream.CopyToAsync(fileStream);
                await fileStream.FlushAsync();
            }

            Console.WriteLine($"Output audio saved to {outputAudioPath}");
            await PlayAudioFile(outputAudioPath);
            
            // Close and remove the audio stream from the dictionary before continuing
            try
            {
                await outputAudioStream.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing audio stream");
            }
        }

        private static byte[] CreateWavHeader(int audioDataLength)
        {
            var wavHeader = new byte[44];
            int sampleRate = 22050;
            short bitsPerSample = 16;
            short channels = 1;
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            int blockAlign = channels * (bitsPerSample / 8);
            int subChunk2Size = audioDataLength;
            int chunkSize = 36 + subChunk2Size;

            // RIFF header
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("RIFF"), 0, wavHeader, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(chunkSize), 0, wavHeader, 4, 4);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("WAVE"), 0, wavHeader, 8, 4);

            // fmt subchunk
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("fmt "), 0, wavHeader, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(16), 0, wavHeader, 16, 4); // Subchunk1Size (16 for PCM)
            Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, wavHeader, 20, 2); // AudioFormat (1 for PCM)
            Buffer.BlockCopy(BitConverter.GetBytes(channels), 0, wavHeader, 22, 2); // NumChannels
            Buffer.BlockCopy(BitConverter.GetBytes(sampleRate), 0, wavHeader, 24, 4); // SampleRate
            Buffer.BlockCopy(BitConverter.GetBytes(byteRate), 0, wavHeader, 28, 4); // ByteRate
            Buffer.BlockCopy(BitConverter.GetBytes(blockAlign), 0, wavHeader, 32, 2); // BlockAlign
            Buffer.BlockCopy(BitConverter.GetBytes(bitsPerSample), 0, wavHeader, 34, 2); // BitsPerSample

            // data subchunk
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("data"), 0, wavHeader, 36, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(subChunk2Size), 0, wavHeader, 40, 4);

            return wavHeader;
        }

        private async Task PlayAudioFile(string outputAudioPath)
        {
            string playerCommand;
            string playerArgs;

            if (OperatingSystem.IsWindows())
            {
                playerCommand = "powershell";
                playerArgs = $"-c (New-Object Media.SoundPlayer '{outputAudioPath}').PlaySync()";
            }
            else if (OperatingSystem.IsMacOS())
            {
                playerCommand = "afplay";
                playerArgs = $"\"{outputAudioPath}\"";
            }
            else // Linux
            {
                playerCommand = "aplay";
                playerArgs = $"-q \"{outputAudioPath}\"";
            }

            var playProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = playerCommand,
                    Arguments = playerArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                }
            };
            
            try
            {
                playProcess.Start();
                await playProcess.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error playing audio file {FilePath}", outputAudioPath);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error playing audio: {ex.Message}");
                Console.ResetColor();
            }
        }

        private static void HandleConversationError(ConversationErrorUpdate errorUpdate)
        {
            Console.WriteLine();
            Console.WriteLine($"ERROR: {errorUpdate.Message}");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources
                CleanupSession().GetAwaiter().GetResult();
                _voiceHandler.Dispose();
                _sessionLock.Dispose();                // Clear transcription deduplication data
                lock (_transcriptionLock)
                {
                    _recentTranscriptions.Clear();
                    _lastRecordingStartTime = DateTime.MinValue;
                    _pendingTranscription = null;
                    _lastTranscriptionDisplayTime = DateTime.MinValue;
                }
                
                // Clear response state
                lock (_responseLock)
                {
                    _responseInProgress = false;
                }
                
                // Note: RealtimeConversationClient doesn't implement IDisposable
            }

            _disposed = true;
        }
    }
}
