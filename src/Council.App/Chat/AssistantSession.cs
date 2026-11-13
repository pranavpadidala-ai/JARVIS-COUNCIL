using Council.AI.Providers;
using Council.App;
using Council.App.Finance;
using Council.Core.Models;
using Council.Core.Providers;
using Council.Memory;

namespace Council.App.Chat;

/// <summary>
/// Desktop facade over the same full tool-calling JARVIS runtime used by the
/// console app. Conversation and teaching memory are stored locally with DPAPI.
/// </summary>
public sealed class AssistantSession
{
    private const string MemoryHelp =
        "Teach JARVIS with /remember followed by a fact. Use /memories to review saved facts and /forget followed by an ID to remove one. Never save passwords, API keys, OTPs or other secrets.";

    private readonly IAIProvider _provider;
    private readonly ConversationMemoryStore _memory;
    private readonly MemoryFactStore _facts;
    private readonly ToolCallingAssistant _assistant;
    private List<AIMessage> _history;

    public string ProviderName => _provider.Name;
    public int MessageCount => _history.Count;

    private AssistantSession(
        IAIProvider provider,
        ConversationMemoryStore memory,
        MemoryFactStore facts,
        ToolCallingAssistant assistant,
        List<AIMessage> history)
    {
        _provider = provider;
        _memory = memory;
        _facts = facts;
        _assistant = assistant;
        _history = history;
    }

    public static async Task<AssistantSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = AppContext.BaseDirectory;

        var memoryPath = Path.Combine(localData, "JARVIS", "Memory", "desktop-conversation.jsonl");
        var memory = new ConversationMemoryStore(memoryPath);
        var facts = new MemoryFactStore(memory.FilePath + ".facts.jsonl");
        var history = (await memory.LoadRecentAsync(120, cancellationToken)).ToList();

        var workspaceRoot = Environment.GetEnvironmentVariable("JARVIS_WORKSPACE_ROOT");
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            workspaceRoot = Directory.GetCurrentDirectory();

        IImageUnderstandingProvider? imageProvider = null;
        IImageUnderstandingProvider? documentImageProvider = null;
        var visionChoice = (Environment.GetEnvironmentVariable("JARVIS_VISION_PROVIDER") ?? "ollama")
            .Trim().ToLowerInvariant();

        switch (visionChoice)
        {
            case "ollama":
                if (!string.Equals(Environment.GetEnvironmentVariable("JARVIS_LOCAL_VISION_ENABLED"), "false", StringComparison.OrdinalIgnoreCase))
                {
                    var baseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";
                    var visionModel = Environment.GetEnvironmentVariable("JARVIS_VISION_MODEL") ?? "qwen3-vl:2b";
                    var documentModel = Environment.GetEnvironmentVariable("JARVIS_DOCUMENT_VISION_MODEL") ?? "glm-ocr";
                    imageProvider = new OllamaVisionProvider(visionModel, baseUrl, contextTokens: 4096);
                    documentImageProvider = new OllamaVisionProvider(documentModel, baseUrl, contextTokens: 4096);
                }
                break;

            case "gemini":
                var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                if (string.IsNullOrWhiteSpace(geminiKey))
                    throw new InvalidOperationException("GEMINI_API_KEY is required when JARVIS_VISION_PROVIDER=gemini.");
                imageProvider = new GeminiProvider(geminiKey, Environment.GetEnvironmentVariable("GEMINI_VISION_MODEL") ?? "gemini-3.8-flash");
                documentImageProvider = imageProvider;
                break;

            case "none":
                break;

            default:
                throw new InvalidOperationException("JARVIS_VISION_PROVIDER must be ollama, gemini, or none.");
        }

        var assistant = new ToolCallingAssistant(
            provider,
            workspaceRoot,
            memory,
            imageProvider,
            cloudSpeechProvider: null,
            indianMarketTool: new IndianMarketTool(),
            documentImageProvider: documentImageProvider);

        return new AssistantSession(provider, memory, facts, assistant, history);
    }

    public async Task<AIResponse> SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A message is required.", nameof(text));

        var prompt = text.Trim();
        var memoryResponse = await TryHandleMemoryCommandAsync(prompt, cancellationToken);
        if (memoryResponse is not null)
            return memoryResponse;

        var started = System.Diagnostics.Stopwatch.StartNew();
        var response = await _assistant.ProcessMessageAsync(prompt, cancellationToken);
        started.Stop();
        _history = (await _memory.LoadRecentAsync(120, cancellationToken)).ToList();
        return new AIResponse(_provider.Name, response, _provider.Name, started.Elapsed);
    }

    public IReadOnlyList<AIMessage> GetRecentMessages(int count = 30) =>
        _history.TakeLast(Math.Clamp(count, 1, 120)).ToArray();

    public async Task ClearAsync()
    {
        await _assistant.ClearConversationAsync();
        _history.Clear();
    }

    private async Task<AIResponse?> TryHandleMemoryCommandAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        string? message = null;
        if (prompt.StartsWith("/remember ", StringComparison.OrdinalIgnoreCase))
        {
            var fact = prompt["/remember ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(fact))
                message = "Usage: /remember followed by a fact or preference.";
            else
            {
                var id = await _facts.AddAsync(fact, cancellationToken);
                message = $"Saved to your private, encrypted JARVIS memory (ID: {id}). Use /memories to review it.";
            }
        }
        else if (prompt.Equals("/remember", StringComparison.OrdinalIgnoreCase) ||
                 prompt.Equals("/teach", StringComparison.OrdinalIgnoreCase) ||
                 prompt.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            message = MemoryHelp;
        }
        else if (prompt.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            await _assistant.ClearConversationAsync(cancellationToken);
            _history.Clear();
            message = "Conversation history and explicitly saved facts were cleared. Learned project practices were retained.";
        }
        else if (prompt.Equals("/memories", StringComparison.OrdinalIgnoreCase))
        {
            var facts = await _facts.ListAsync(cancellationToken);
            message = facts.Count == 0
                ? "You have not taught JARVIS any long-term facts yet. " + MemoryHelp
                : "Your saved JARVIS memories:\n" + string.Join("\n", facts.Select(fact => $"[{fact.Id}] {fact.Fact}"));
        }
        else if (prompt.StartsWith("/forget ", StringComparison.OrdinalIgnoreCase))
        {
            var id = prompt["/forget ".Length..].Trim();
            var removed = !string.IsNullOrWhiteSpace(id) &&
                await _facts.RemoveAsync(id, cancellationToken);
            message = removed
                ? "That saved memory was removed."
                : "No saved memory was found with that ID. Use /memories to see IDs.";
        }

        return message is null
            ? null
            : new AIResponse("JARVIS Memory", message, "local-encrypted-memory", TimeSpan.Zero);
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
                    throw new InvalidOperationException("Set OMNIROUTE_MODEL and OMNIROUTE_FREE_MODELS to exact approved model IDs. Automatic paid routing is disabled.");
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
