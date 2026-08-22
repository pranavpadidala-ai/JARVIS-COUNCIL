#pragma warning disable OPENAI001

using System.Diagnostics;
using Council.Core.Models;
using Council.Core.Providers;
using OpenAI.Responses;

namespace Council.AI.Providers;

public sealed class OpenAIProvider : IAIProvider
{
    private readonly ResponsesClient _client;
    private readonly string _model;

    public string Name => "OpenAI";

    public OpenAIProvider(string apiKey, string model = "gpt-5.2")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException(
                "OpenAI API key is required.",
                nameof(apiKey));
        }

        _client = new ResponsesClient(apiKey);
        _model = model;
    }

    public async Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var userMessage = request.Messages
            .LastOrDefault(message =>
                string.Equals(
                    message.Role,
                    "user",
                    StringComparison.OrdinalIgnoreCase));

        if (userMessage is null)
        {
            throw new ArgumentException(
                "The request must contain at least one user message.",
                nameof(request));
        }

        var response = await _client.CreateResponseAsync(
            _model,
            userMessage.Content,
            cancellationToken: cancellationToken);

        stopwatch.Stop();

        return new AIResponse(
            Provider: Name,
            Content: response.Value.GetOutputText(),
            Model: _model,
            Duration: stopwatch.Elapsed);
    }
}
