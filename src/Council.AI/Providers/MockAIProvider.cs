using System.Diagnostics;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

public sealed class MockAIProvider : IAIProvider
{
    public string Name => "Mock";

    public Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var lastUserMessage = request.Messages
            .LastOrDefault(message => 
                string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase));

        var question = lastUserMessage?.Content ?? "(no user message)";

        stopwatch.Stop();

        var response = new AIResponse(
            Provider: Name,
            Content: $"Mock response received for: {question}",
            Model: "mock-v0.1",
            Duration: stopwatch.Elapsed);

        return Task.FromResult(response);
    }
}
