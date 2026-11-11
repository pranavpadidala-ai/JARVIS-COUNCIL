#pragma warning disable OPENAI001

using System.Diagnostics;
using Council.Core.Models;
using Council.Core.Providers;
using OpenAI.Responses;

namespace Council.AI.Providers;

public sealed class OpenAIProvider : IAIProvider
{
    private readonly ResponsesClient _client;
    private readonly string _defaultModel;

    public string Name => "OpenAI";

    public OpenAIProvider(
        string apiKey,
        string model = "gpt-5.2")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "OpenAI API key is required.", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException(
                "OpenAI model is required.", nameof(model));

        _client = new ResponsesClient(apiKey);
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

        var options = new CreateResponseOptions
        {
            Model = model
        };

        var hasUserMessage = false;

        foreach (var message in request.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Content))
                continue;

            var role = message.Role?.Trim().ToLowerInvariant();

            var item = role switch
            {
                "user" =>
                    ResponseItem.CreateUserMessageItem(message.Content),

                "assistant" =>
                    ResponseItem.CreateAssistantMessageItem(message.Content),

                "system" =>
                    ResponseItem.CreateSystemMessageItem(message.Content),

                "developer" =>
                    ResponseItem.CreateDeveloperMessageItem(message.Content),

                _ => throw new ArgumentException(
                    $"Unsupported OpenAI message role: {message.Role}")
            };

            if (role == "user")
                hasUserMessage = true;

            options.InputItems.Add(item);
        }

        if (!hasUserMessage)
            throw new ArgumentException(
                "At least one user message is required.", nameof(request));

        var response = await _client.CreateResponseAsync(
            options, cancellationToken);

        stopwatch.Stop();

        var content = response.Value.GetOutputText();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                "OpenAI returned an empty response.");

        return new AIResponse(
            Provider: Name,
            Content: content,
            Model: model,
            Duration: stopwatch.Elapsed);
    }
}
