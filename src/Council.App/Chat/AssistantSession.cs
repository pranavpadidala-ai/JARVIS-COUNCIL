using Council.AI.Providers;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.App.Chat;

/// <summary>
/// Shared chat runtime used by the console and Windows desktop experiences.
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
        var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var omniKey = Environment.GetEnvironmentVariable("OMNIROUTE_API_KEY");
        var omniBaseUrl = Environment.GetEnvironmentVariable("OMNIROUTE_BASE_URL");
        var selection = Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER")?.Trim().ToLowerInvariant();

        var providers = new List<IAIProvider>();
        void Gemini()
        {
            if (!string.IsNullOrWhiteSpace(geminiKey))
                providers.Add(new GeminiProvider(geminiKey, Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.6-flash"));
        }
        void OpenAi()
        {
            if (!string.IsNullOrWhiteSpace(openAiKey))
                providers.Add(new OpenAIProvider(openAiKey, Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.2"));
        }
        void Omni()
        {
            if (!string.IsNullOrWhiteSpace(omniKey) && !string.IsNullOrWhiteSpace(omniBaseUrl))
                providers.Add(new OpenAICompatibleProvider(omniBaseUrl, omniKey, Environment.GetEnvironmentVariable("OMNIROUTE_MODEL") ?? "auto", "OmniRoute"));
        }

        switch (selection)
        {
            case "openai": OpenAi(); Gemini(); Omni(); break;
            case "omniroute":
            case "compatible": Omni(); Gemini(); OpenAi(); break;
            case null or "":
            case "gemini": Gemini(); Omni(); OpenAi(); break;
            default: throw new InvalidOperationException($"Unknown JARVIS_AI_PROVIDER '{selection}'.");
        }

        return providers.Count switch
        {
            0 => new MockAIProvider(),
            1 => providers[0],
            _ => new FallbackAIProvider(providers)
        };
    }
}