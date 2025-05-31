using System;
using Microsoft.Extensions.Logging;
#pragma warning disable OPENAI002
using OpenAI.RealtimeConversation;
#pragma warning restore OPENAI002

namespace semanticKernelSample1.Assistant
{
    /// <summary>
    /// Helper class for checking transcription quality
    /// </summary>
    public static class TranscriptionHelper
    {        /// <summary>
        /// Validates transcription content and provides feedback if it's empty or too short
        /// </summary>
        #pragma warning disable OPENAI002
        public static bool ValidateTranscription(ConversationInputTranscriptionFinishedUpdate transcription, ILogger logger)
        #pragma warning restore OPENAI002
        {
            // Check for empty or very short transcripts
            if (string.IsNullOrWhiteSpace(transcription.Transcript) || 
                transcription.Transcript.Trim().Length < 3)
            {
                // Log the issue
                logger.LogWarning("Empty or very short transcript detected");
                
                // Provide visual feedback
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("The recording appears to be empty or very short. Please try again with a clear voice.");
                Console.ResetColor();
                
                return false;
            }
            
            return true;
        }
    }
}
