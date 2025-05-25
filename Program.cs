using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using semanticKernelSample1.Assistant;
using semanticKernelSample1.Plugins;
using semanticKernelSample1.Extensions;

Console.WriteLine("Starting GPT Voice Assistant...");

// Register configuration and services
var services = new ServiceCollection().AddAppServices();
var serviceProvider = services.BuildServiceProvider();

var configuration = serviceProvider.GetRequiredService<IConfiguration>();
var modelId = configuration["ModelId"] ?? throw new InvalidOperationException("ModelId not found in configuration");
var apiKey = configuration["OpenAIKey"] ?? throw new InvalidOperationException("OpenAIKey not found in configuration");

// Create kernel with OpenAI chat completion
var builder = Kernel.CreateBuilder().AddOpenAIChatCompletion(modelId, apiKey);
var kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Register plugins with the kernel using our service provider
kernel.Plugins.AddFromType<ApiAlphaPlugin>("ApiAlpha", serviceProvider);

// Get logger and create assistant
var logger = serviceProvider.GetRequiredService<ILogger<GptAssistant>>();

// Log available plugins and functions for debugging
var appLogger = serviceProvider.GetRequiredService<ILogger<Program>>();
appLogger.LogInformation("Available plugins: {PluginCount}", kernel.Plugins.Count);
foreach (var plugin in kernel.Plugins)
{
    appLogger.LogInformation("Plugin: {PluginName}", plugin.Name);
    foreach (var function in plugin.GetFunctionsMetadata())
    {
        appLogger.LogInformation("  Function: {FunctionName} - {Description}", function.Name, function.Description);
    }
}

// Create and run the voice-enabled GPT assistant
var assistant = new GptAssistant(kernel, chatCompletionService, logger, apiKey);

Console.WriteLine("GPT Voice Assistant with API Alpha Plugin is ready!");
Console.WriteLine("You will be able to speak to the assistant using your microphone.");
Console.WriteLine("Follow the prompts to start recording.\n");

// Run the GPT assistant
await assistant.RunAsync();