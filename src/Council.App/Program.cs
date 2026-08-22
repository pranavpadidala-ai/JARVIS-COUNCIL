using Council.AI.Providers;
using Council.Core.Models;
using Council.Core.Orchestration;
using Council.Core.Providers;

var providers = new List<IAIProvider>();

// Always keep the mock provider available for offline testing.
providers.Add(new MockAIProvider());

// Add Gemini when an API key is configured.
var geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

if (!string.IsNullOrWhiteSpace(geminiApiKey))
{
    providers.Add(new GeminiProvider(
        geminiApiKey,
        model: "gemini-3.6-flash"));
}
else
{
    Console.WriteLine("Warning: GEMINI_API_KEY is not configured.");
}

var council = new CouncilOrchestrator(providers);

var request = new AIRequest(
    Messages:
    [
        new AIMessage(
            "user",
            "What is the purpose of JARVIS Council?")
    ]);

var responses = await council.AskAllAsync(request);

foreach (var response in responses)
{
    Console.WriteLine();
    Console.WriteLine($"Provider : {response.Provider}");
    Console.WriteLine($"Model    : {response.Model}");
    Console.WriteLine($"Response : {response.Content}");
    Console.WriteLine($"Duration : {response.Duration}");
}