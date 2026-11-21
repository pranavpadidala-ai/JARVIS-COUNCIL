using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// Anthropic Claude through the official Anthropic SDK.
/// System messages become the system prompt; user and assistant messages keep their order.
/// Server-side refusal fallback is on, so a request a safety check declines is
/// answered by a fallback model inside the same call instead of failing.
/// </summary>
public sealed class ClaudeProvider : IAIProvider
{
    public const string DefaultModel = "claude-opus-5-5";
    private const int MaxOutputTokens = 16000;

    private readonly AnthropicClient _client;
    private readonly string _defaultModel;

    public string Name => "Claude";

    public ClaudeProvider(string apiKey, string model = DefaultModel)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Anthropic API key is required.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Claude model is required.", nameof(model));

        _client = new AnthropicClient { ApiKey = apiKey };
        _defaultModel = model;
    }

    public async Task<AIResponse> AskAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var (system, turns) = BuildConversation(request);
        var model = string.IsNullOrWhiteSpace(request.Model) ? _defaultModel : request.Model!;
        var stopwatch = Stopwatch.StartNew();

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = MaxOutputTokens,
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            Messages = turns.Select(ToParam).ToList(),
        };
        if (!string.IsNullOrWhiteSpace(system))
            parameters = parameters with { System = system };

        var response = await _client.Beta.Messages.Create(parameters, cancellationToken);
        stopwatch.Stop();

        if (response.StopReason == BetaStopReason.Refusal)
            throw new InvalidOperationException("Claude declined this request.");

        var content = string.Join("\n", response.Content
            .Select(block => block.Value)
            .OfType<BetaTextBlock>()
            .Select(text => text.Text)).Trim();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Claude returned an empty response.");

        return new AIResponse(Name, content, model, stopwatch.Elapsed);
    }

    /// <summary>
    /// Splits the shared chat history into a system prompt and alternating turns.
    /// A trailing assistant message is dropped because current Claude models reject
    /// a conversation that ends on the assistant's side.
    /// </summary>
    public static (string? System, IReadOnlyList<(string Role, string Text)> Turns) BuildConversation(AIRequest request)
    {
        if (request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var systemParts = new List<string>();
        var turns = new List<(string Role, string Text)>();
        foreach (var message in request.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Content))
                continue;
            var role = message.Role?.Trim().ToLowerInvariant();
            switch (role)
            {
                case "system":
                case "developer":
                    systemParts.Add(message.Content.Trim());
                    break;
                case "user":
                case "assistant":
                    if (turns.Count > 0 && turns[^1].Role == role)
                        turns[^1] = (role, turns[^1].Text + "\n\n" + message.Content);
                    else
                        turns.Add((role, message.Content));
                    break;
                default:
                    throw new ArgumentException($"Unsupported Claude message role: {message.Role}");
            }
        }

        while (turns.Count > 0 && turns[^1].Role == "assistant")
            turns.RemoveAt(turns.Count - 1);
        while (turns.Count > 0 && turns[0].Role == "assistant")
            turns.RemoveAt(0);
        if (turns.Count == 0)
            throw new ArgumentException("At least one user message is required.", nameof(request));

        var system = systemParts.Count == 0 ? null : string.Join("\n\n", systemParts);
        return (system, turns);
    }

    private static BetaMessageParam ToParam((string Role, string Text) turn) =>
        turn.Role == "user"
            ? new BetaUserMessageParam { Content = turn.Text }
            : new BetaAssistantMessageParam { Content = turn.Text };
}
