using System.ClientModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.RealtimeConversation;
using semanticKernelSample1;

namespace semanticKernelSample1.Assistant
{    public class GptAssistant
    {
        private readonly Kernel _kernel;
        private readonly ILogger<GptAssistant> _logger;
        private bool _useTextMode = false;
        private readonly VoiceHandler _voiceHandler;
          
        #pragma warning disable OPENAI002
        private readonly RealtimeConversationClient _realTimeConversationClient;
        
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
        }        private bool IsSoxAvailable()
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
                Console.ReadKey();
                _useTextMode = true;
            }            // Let the user select a voice
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

                await session.AddItemAsync(ConversationItem.CreateSystemMessage(["You are a helpful digital assistant named GPT Voice Assistant. Always respond with a clear greeting and introduce yourself in your first response. Keep your responses concise and direct. You can use the plugins and tools provided to assist the user. Always respond to the user's queries even if they seem incomplete - do your best to understand the intent."]));

                #pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                GatherResponses(_kernel, session);
                #pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                if (_useTextMode)
                {
                    Console.WriteLine("GPT Assistant in TEXT MODE is ready!");
                    Console.WriteLine("You can ask me about base character names from the API.");
                    Console.WriteLine("Type 'exit' or leave empty to quit.\n");

                    // Run text-based interaction loop
                    await RunTextModeAsync(session);
                }
                else
                {
                    Console.WriteLine("GPT Voice Assistant is ready!");
                    Console.WriteLine("You can ask me about base character names from the API.");
                    Console.WriteLine("Press Enter to start recording, and press Enter again to stop.\n");

                    // Run voice-based interaction loop
                    await RunVoiceModeAsync(session);
                }            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in the GPT assistant");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An error occurred in the GPT assistant: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                // Clean up temporary audio files before exiting
                Console.WriteLine("Cleaning up temporary audio files...");
                _voiceHandler.CleanupTempFiles();
                Console.WriteLine("Cleanup complete. Goodbye!");
            }
        }

        private async Task RunTextModeAsync(RealtimeConversationSession session)
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
                
                // Add text input to session
                await session.AddItemAsync(ConversationItem.CreateUserMessage([input]));
                
                // Start response generation
                await session.StartResponseAsync();
                
                // The responses will be handled by the GatherResponses method
            }
        }        private async Task RunVoiceModeAsync(RealtimeConversationSession session)
        {
            do
            {
                Console.WriteLine("\nPress Enter to start recording... Press Enter again to stop.");
                Console.WriteLine("Type 'voice' to change the assistant's voice, or 'exit' to quit.");
                
                string? input = Console.ReadLine();
                
                // Handle voice change command
                if (input?.Equals("voice", StringComparison.OrdinalIgnoreCase) == true)
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
                    continue;
                }                // Handle exit command
                else if (input?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true || 
                         input?.Equals("quit", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Clean up before exiting
                    Console.WriteLine("Cleaning up temporary files before exiting...");
                    _voiceHandler.CleanupTempFiles();
                    Console.WriteLine("Cleanup complete. Goodbye!");
                    break;
                }
                // Handle direct text input (if not empty)
                else if (!string.IsNullOrEmpty(input))
                {
                    // Send text input instead of audio
                    try
                    {
                        await session.AddItemAsync(ConversationItem.CreateUserMessage([input]));
                        await session.StartResponseAsync();
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Error sending text input: {ex.Message}");
                        Console.ResetColor();
                        continue;
                    }
                }
                
                // Handle audio recording (when user just presses Enter)
                try
                {
                    // Use the VoiceHandler to record and process audio
                    await _voiceHandler.RecordAndProcessAudioAsync(session);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing audio");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();                }
            } while (true);
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
        }

        private async Task GatherResponses(Kernel kernel, RealtimeConversationSession session)
        {
            try 
            {
                // Initialize dictionaries to store streamed audio responses and function arguments.
                Dictionary<string, MemoryStream> outputAudioStreamsById = [];
                Dictionary<string, StringBuilder> functionArgumentBuildersById = [];

                // Define a loop to receive conversation updates in the session.
                await foreach (ConversationUpdate update in session.ReceiveUpdatesAsync())
                {
                    // Notification indicating the start of the conversation session.
                    if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                    {
                        Console.WriteLine($"<<< Session started. ID: {sessionStartedUpdate.SessionId}");
                        Console.WriteLine();
                    }

                    // Notification indicating the start of detected voice activity.
                    if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                    {
                        Console.WriteLine($"  -- Voice activity detection started at {speechStartedUpdate.AudioStartTime}");
                    }

                    // Notification indicating the end of detected voice activity.
                    if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                    {
                        Console.WriteLine($"  -- Voice activity detection ended at {speechFinishedUpdate.AudioEndTime}");
                    }

                    // Notification indicating the start of item streaming, such as a function call or response message.
                    if (update is ConversationItemStreamingStartedUpdate itemStreamingStartedUpdate)
                    {
                        Console.WriteLine("  -- Begin streaming of new item");
                        if (!string.IsNullOrEmpty(itemStreamingStartedUpdate.FunctionName))
                        {
                            Console.Write($"    {itemStreamingStartedUpdate.FunctionName}: ");
                        }
                    }

                    // Notification about item streaming delta, which may include audio transcript, audio bytes, or function arguments.
                    if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate)
                    {
                        Console.Write(deltaUpdate.AudioTranscript);
                        Console.Write(deltaUpdate.Text);
                        Console.Write(deltaUpdate.FunctionArguments);

                        // Handle audio bytes.                            if (deltaUpdate.AudioBytes is not null)
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

                    // Notification indicating the end of item streaming, such as a function call or response message.
                    if (update is ConversationItemStreamingFinishedUpdate itemStreamingFinishedUpdate)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  -- Item streaming finished, item_id={itemStreamingFinishedUpdate.ItemId}");

                        // If an item is a function call, invoke a function with provided arguments.
                        if (itemStreamingFinishedUpdate.FunctionCallId is not null)
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
                        }                        // If an item is a response message, output it to the console.
                        else if (itemStreamingFinishedUpdate.MessageContentParts?.Count > 0)
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
                            }

                            Console.ResetColor();
                            Console.WriteLine();
                        }
                    }                    // Notification indicating the completion of transcription from input audio.
                    if (update is ConversationInputTranscriptionFinishedUpdate transcriptionCompletedUpdate)
                    {
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("User said: ");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine(transcriptionCompletedUpdate.Transcript);
                        Console.ResetColor();
                        Console.WriteLine();
                    }

                    // Notification about completed model response turn.
                    if (update is ConversationResponseFinishedUpdate turnFinishedUpdate)
                    {
                        Console.WriteLine($"  -- Model turn generation finished. Status: {turnFinishedUpdate.Status}");

                        // If the created session items contain a function name, it indicates a function call result has been provided,
                        // and response updates can begin.
                        if (turnFinishedUpdate.CreatedItems.Any(item => item.FunctionName?.Length > 0))
                        {
                            Console.WriteLine("  -- Ending client turn for pending tool responses");

                            await session.StartResponseAsync();
                        }
                        // Otherwise, the model's response is provided, signaling that updates can be stopped.
                        else
                        {
                            // Output the size of received audio data and dispose streams.
                            foreach ((string itemId, Stream outputAudioStream) in outputAudioStreamsById)
                            {
                                Console.WriteLine($"Raw audio output for {itemId}: {outputAudioStream.Length} bytes");

                                // Convert raw PCM data to WAV format
                                var wavHeader = new byte[44];
                                int sampleRate = 22050;
                                short bitsPerSample = 16;
                                short channels = 1;
                                int byteRate = sampleRate * channels * (bitsPerSample / 8);
                                int blockAlign = channels * (bitsPerSample / 8);
                                int subChunk2Size = (int)outputAudioStream.Length;
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
                                Buffer.BlockCopy(BitConverter.GetBytes(subChunk2Size), 0, wavHeader, 40, 4);                                // Create a new memory stream for the WAV file
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
                                }                                var playProcess = new Process
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
                                
                                // Close and remove the audio stream from the dictionary before continuing
                                try
                                {
                                    await outputAudioStream.DisposeAsync();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Error disposing audio stream");
                                }
                                
                                outputAudioStreamsById.Remove(itemId);
                            }
                        }
                    }

                    // Notification about error in conversation session.
                    if (update is ConversationErrorUpdate errorUpdate)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"ERROR: {errorUpdate.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in GatherResponses: {e.Message}");
                _logger.LogError(e, "Error in GatherResponses");
            }
        }
    }
}
