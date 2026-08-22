using System.Diagnostics;
using Council.Core.Models;
using Council.Core.Providers;
using Google.GenAI;

namespace Council.AI.Providers;

public sealed class GeminiProvider : IAIProvider
{
    private readonly Client _client;
    private readonly string _model;

    public string Name => "Gemini";

    public GeminiProvider(
        string apiKey,
        string model = "gemini-3.6-flash")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException(
                "Gemini API key is required.",
                nameof(apiKey));
        }

        _client = new Client(apiKey: apiKey);
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

        var model = request.Model ?? _model;

        var response = await _client.Models.GenerateContentAsync(
            model: model,
            contents: userMessage.Content);

        stopwatch.Stop();

        var content =
            response.Candidates?
                .FirstOrDefault()?
                .Content?
                .Parts?
                .FirstOrDefault()?
                .Text;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "Gemini returned an empty response.");
        }

        return new AIResponse(
            Provider: Name,
            Content: content,
            Model: model,
            Duration: stopwatch.Elapsed);
    }
}