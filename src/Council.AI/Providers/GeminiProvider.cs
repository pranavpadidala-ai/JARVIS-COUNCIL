using System.Diagnostics;
using Council.Core.Models;
using Council.Core.Providers;
using Google.GenAI;
using Google.GenAI.Types;

namespace Council.AI.Providers;

public sealed class GeminiProvider : IAIProvider
{
    private readonly Client _client;
    private readonly string _defaultModel;

    public string Name => "Gemini";

    public GeminiProvider(
        string apiKey,
        string model = "gemini-3.6-flash")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "Gemini API key is required.", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException(
                "Gemini model is required.", nameof(model));

        _client = new Client(apiKey: apiKey);
        _defaultModel = model;
    }

    public async Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException(
                "At least one message is required.", nameof(request));

        var stopwatch = Stopwatch.StartNew();
        var model = request.Model ?? _defaultModel;

        var systemMessages = request.Messages
            .Where(m =>
                IsRole(m.Role, "system") ||
                IsRole(m.Role, "developer"))
            .Select(m => m.Content);

        var systemInstruction = string.Join(
            "\n\n", systemMessages);

        var contents = new List<Content>();

        foreach (var message in request.Messages)
        {
            var role = message.Role?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(message.Content))
                continue;

            if (role is "system" or "developer")
                continue;

            if (role is not ("user" or "assistant" or "model"))
                throw new ArgumentException(
                    $"Unsupported Gemini message role: {message.Role}");

            contents.Add(new Content
            {
                Role = role == "user" ? "user" : "model",
                Parts = [new Part { Text = message.Content }]
            });
        }

        if (contents.Count == 0 ||
            !contents.Any(c => c.Role == "user"))
            throw new ArgumentException(
                "At least one user message is required.", nameof(request));

        var config = new GenerateContentConfig
        {
            Temperature = request.Temperature
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            config.SystemInstruction = new Content
            {
                Parts = [new Part { Text = systemInstruction }]
            };
        }

        var response = await _client.Models.GenerateContentAsync(
            model,
            contents,
            config,
            cancellationToken);

        stopwatch.Stop();

        var output = response.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .Select(p => p.Text)
            .ToList();

        var content = output is { Count: > 0 }
            ? string.Join("", output)
            : null;

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                "Gemini returned an empty response.");

        return new AIResponse(
            Provider: Name,
            Content: content,
            Model: model,
            Duration: stopwatch.Elapsed);
    }

    private static bool IsRole(string? actual, string expected) =>
        string.Equals(
            actual, expected,
            StringComparison.OrdinalIgnoreCase);
}
