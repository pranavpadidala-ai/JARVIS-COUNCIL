using Council.AI.Providers;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.App.Chat;

/// <summary>
/// Shared chat runtime used by the Windows desktop experience.
/// Provider selection intentionally matches the main JARVIS application:
/// an explicitly selected provider never silently falls back to another service.
/// </summary>
public sealed class AssistantSession
{
    private const string Instructions = """
        You are JARVIS, the user's practical personal AI assistant.
        Be useful, accurate, direct and transparent about uncertainty.
        Use conversation history when relevant. Never claim to have opened apps,
        changed files, searched live markets or performed actions unless a connected
        tool actually completed that action. Never request or expose API secrets.
        """;

    private readonly ConversationStore _store;
    private readonly IAIProvider _provider;
    private readonly List<AIMessage> _history;

    public string ProviderName => _provider.Name;
    public int MessageCount => _history.Count;

    private AssistantSession(ConversationStore store, IAIProvider provider, List<AIMessage> history)
    {
        _store = store;
        _provider = provider;
        _history = history;
    }

    public static async Task<AssistantSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        var store = new ConversationStore();
        var history = await store.LoadAsync(cancellationToken);
        return new AssistantSession(store, CreateProvider(), history);
    }

    public async Task<AIResponse> SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A message is required.", nameof(text));

        _history.Add(new AIMessage("user", text.Trim()));
        await _store.SaveAsync(_history, cancellationToken);

        var messages = new List<AIMessage> { new("system", Instructions) };
        messages.AddRange(_history.TakeLast(40));

        var response = await _provider.AskAsync(
            new AIRequest(messages, Temperature: 0.35),
            cancellationToken);

        _history.Add(new AIMessage("assistant", response.Content));
        if (_history.Count > 120)
            _history.RemoveRange(0, _history.Count - 120);
        await _store.SaveAsync(_history, cancellationToken);
        return response;
    }

    public IReadOnlyList<AIMessage> GetRecentMessages(int count = 30) =>
        _history.TakeLast(Math.Clamp(count, 1, 120)).ToArray();

    public async Task ClearAsync()
    {
        _history.Clear();
        await _store.ClearAsync();
    }

    private static IAIProvider CreateProvider()
    {
        var selection = Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER")?.Trim().ToLowerInvariant();
        var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        switch (selection)
        {
            case "ollama":
                var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "qwen3.5:2b-q4_K_M";
                var baseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";
                var contextTokens = int.TryParse(
                    Environment.GetEnvironmentVariable("JARVIS_OLLAMA_CONTEXT_TOKENS"), out var configuredContext)
                    ? Math.Clamp(configuredContext, 2048, 8192)
                    : 4096;
                return new OllamaProvider(model, baseUrl, contextTokens);

            case "gemini":
                if (string.IsNullOrWhiteSpace(geminiKey))
                    throw new InvalidOperationException("GEMINI_API_KEY is required when JARVIS_AI_PROVIDER=gemini.");
                return new GeminiProvider(geminiKey, Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.6-flash");

            case "openai":
                if (string.IsNullOrWhiteSpace(openAiKey))
                    throw new InvalidOperationException("OPENAI_API_KEY is required when JARVIS_AI_PROVIDER=openai.");
                return new OpenAIProvider(openAiKey, Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.2");

            case "omniroute":
                var omniKey = Environment.GetEnvironmentVariable("OMNIROUTE_API_KEY");
                var omniModel = Environment.GetEnvironmentVariable("OMNIROUTE_MODEL");
                var omniBaseUrl = Environment.GetEnvironmentVariable("OMNIROUTE_BASE_URL") ?? "http://localhost:20128/v1";
                var allowedModels = Environment.GetEnvironmentVariable("OMNIROUTE_FREE_MODELS");
                if (string.IsNullOrWhiteSpace(omniKey))
                    throw new InvalidOperationException("OMNIROUTE_API_KEY is required when JARVIS_AI_PROVIDER=omniroute.");
                if (string.IsNullOrWhiteSpace(omniModel) || string.IsNullOrWhiteSpace(allowedModels))
                    throw new InvalidOperationException("Set OMNIROUTE_MODEL and OMNIROUTE_FREE_MODELS to the exact model ID(s) you have approved. Automatic paid routing is disabled.");
                return new OmniRouteProvider(omniKey, omniModel, omniBaseUrl, allowedModels);

            case "mock":
                return new MockAIProvider();

            case null or "":
                if (!string.IsNullOrWhiteSpace(geminiKey))
                    return new GeminiProvider(geminiKey, Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.6-flash");
                if (!string.IsNullOrWhiteSpace(openAiKey))
                    return new OpenAIProvider(openAiKey, Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.2");
                return new MockAIProvider();

            default:
                throw new InvalidOperationException($"Unknown JARVIS_AI_PROVIDER '{selection}'. Choose ollama, gemini, openai, omniroute or mock.");
        }
    }
}
