using OpenAI.RealtimeConversation;

namespace semanticKernelSample1
{
    public static class VoiceOptions
    {
        // Dictionary of available voices with descriptions
        #pragma warning disable OPENAI002
        public static readonly Dictionary<string, ConversationVoice> AvailableVoices = new()
        {
            { "Echo (Default)", ConversationVoice.Echo },
            { "Alloy (Balanced)", ConversationVoice.Alloy }
        };

        // Display voice options and let user choose
        public static ConversationVoice SelectVoice()
        {
            Console.WriteLine("Available voice options:");
            
            // Display all voice options
            int index = 1;
            foreach (var voice in AvailableVoices)
            {
                Console.WriteLine($"{index}. {voice.Key}");
                index++;
            }
            
            Console.Write("\nSelect voice number (press Enter for default): ");
            string? input = Console.ReadLine();
              // Default to Echo if no valid selection
            if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int selection) && 
                selection > 0 && selection <= AvailableVoices.Count)
            {
                #pragma warning disable OPENAI002
                return AvailableVoices.Values.ElementAt(selection - 1);
                #pragma warning restore OPENAI002
            }
            
            Console.WriteLine("Using default voice (Echo)");
            #pragma warning disable OPENAI002
            return ConversationVoice.Echo;
            #pragma warning restore OPENAI002
        }
    }
}
