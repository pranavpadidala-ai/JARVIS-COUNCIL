using Council.AI.Providers;
using Council.App;
using Council.App.Finance;
using Council.App.Phone;
using Council.Core.Models;
using Council.Core.Providers;
using Council.Memory;

namespace Council.App.Chat;

/// <summary>
/// Desktop facade over the same full tool-calling JARVIS runtime used by the
/// console app. Conversation and teaching memory are stored locally with DPAPI.
/// </summary>
public sealed class AssistantSession : IAsyncDisposable
{
    private const string MemoryHelp =
        "Teach JARVIS with /remember followed by a fact. Use /memories to review saved facts and /forget followed by an ID to remove one. Never save passwords, API keys, OTPs or other secrets. Use /learn <trigger> => <action> to teach a project practice, /instincts to review practices, /forget-instinct <id> to remove one, /skills and /agents to inspect JARVIS capabilities, and /clear to clear conversation and explicit facts.";

    private readonly IAIProvider _provider;
    private readonly ConversationMemoryStore _memory;
    private readonly MemoryFactStore _facts;
    private readonly ToolCallingAssistant _assistant;
    private readonly PhoneCommandInbox _phoneInbox;
    private readonly PhoneCommandBridgeService? _phoneBridge;
    private List<AIMessage> _history;

    public string PhoneBridgeStatus { get; }

    public string ProviderName => _provider.Name;
    public int MessageCount => _history.Count;

    private AssistantSession(
        IAIProvider provider,
        ConversationMemoryStore memory,
        MemoryFactStore facts,
        ToolCallingAssistant assistant,
        PhoneCommandInbox phoneInbox,
        PhoneCommandBridgeService? phoneBridge,
        string phoneBridgeStatus,
        List<AIMessage> history)
    {
        _provider = provider;
        _memory = memory;
        _facts = facts;
        _assistant = assistant;
        _phoneInbox = phoneInbox;
        _phoneBridge = phoneBridge;
        PhoneBridgeStatus = phoneBridgeStatus;
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

        var phoneInbox = new PhoneCommandInbox();
        PhoneCommandBridgeService? phoneBridge = null;
        var bridgeBind = Environment.GetEnvironmentVariable("JARVIS_BRIDGE_BIND");
        var bridgeToken = Environment.GetEnvironmentVariable("JARVIS_BRIDGE_TOKEN");
        var phoneBridgeStatus = "Disabled. Configure JARVIS_BRIDGE_BIND and JARVIS_BRIDGE_TOKEN to enable the Tailscale-only phone inbox.";
        if (!string.IsNullOrWhiteSpace(bridgeBind) && !string.IsNullOrWhiteSpace(bridgeToken))
        {
            try
            {
                var bridgePort = int.TryParse(Environment.GetEnvironmentVariable("JARVIS_BRIDGE_PORT"), out var configuredPort)
                    ? configuredPort
                    : 47821;
                phoneBridge = new PhoneCommandBridgeService(bridgeBind, bridgePort, bridgeToken, phoneInbox);
                phoneBridge.Start();
                phoneBridgeStatus = $"Listening on {bridgeBind}:{bridgePort}. Phone requests are queued and require approval in JARVIS.";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                       System.Net.HttpListenerException or UnauthorizedAccessException)
            {
                phoneBridgeStatus = "Not started: " + ex.Message;
                if (phoneBridge is not null)
                    await phoneBridge.DisposeAsync();
                phoneBridge = null;
            }
        }

        return new AssistantSession(
            provider, memory, facts, assistant, phoneInbox, phoneBridge, phoneBridgeStatus, history);
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
        return new AIResponse(_provider.Name, response, _assistant.LastModel ?? _provider.Name, started.Elapsed);
    }

    public Task<IReadOnlyList<PhoneCommand>> ListPhoneCommandsAsync(CancellationToken cancellationToken = default) =>
        _phoneInbox.ListRecentAsync(100, cancellationToken);

    public async Task<AIResponse> ApprovePhoneCommandAsync(
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        var command = await _phoneInbox.ClaimAsync(commandId, cancellationToken);
        if (command is null)
            return new AIResponse(
                "JARVIS Phone",
                "This request is no longer waiting for approval. It may have been cancelled or claimed by another JARVIS session.",
                "phone-inbox",
                TimeSpan.Zero);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _assistant.ProcessMessageAsync(command.Text, cancellationToken);
            await _phoneInbox.CompleteAsync(command.Id, result, CancellationToken.None);
            _history = (await _memory.LoadRecentAsync(120, CancellationToken.None)).ToList();
            timer.Stop();
            return new AIResponse(_provider.Name, result, _assistant.LastModel ?? _provider.Name, timer.Elapsed);
        }
        catch (OperationCanceledException)
        {
            await _phoneInbox.FailAsync(
                command.Id,
                "Request cancelled on the laptop. Any work completed before cancellation remains completed.",
                CancellationToken.None);
            _history = (await _memory.LoadRecentAsync(120, CancellationToken.None)).ToList();
            throw;
        }
        catch (Exception)
        {
            const string failure = "JARVIS encountered an error while processing this phone request. Please retry from the laptop.";
            await _phoneInbox.FailAsync(command.Id, failure, CancellationToken.None);
            timer.Stop();
            return new AIResponse(_provider.Name, failure, _assistant.LastModel ?? _provider.Name, timer.Elapsed);
        }
    }

    public Task<bool> RejectPhoneCommandAsync(Guid commandId, CancellationToken cancellationToken = default) =>
        _phoneInbox.CancelAsync(commandId, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_phoneBridge is not null)
            await _phoneBridge.DisposeAsync();
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
        else if (prompt.Equals("/skills", StringComparison.OrdinalIgnoreCase))
        {
            var skills = _assistant.ListSkills();
            message = skills.Count == 0
                ? "No specialist skills were discovered. Check the installed JARVIS Skills folder."
                : "Available specialist skills:\n" + string.Join("\n", skills.Select(skill => "- " + skill));
        }
        else if (prompt.Equals("/agents", StringComparison.OrdinalIgnoreCase))
        {
            message = await _assistant.ListAgentsAsync(cancellationToken);
        }
        else if (prompt.Equals("/instincts", StringComparison.OrdinalIgnoreCase))
        {
            message = await _assistant.ListLearnedPracticesAsync(cancellationToken);
        }
        else if (prompt.StartsWith("/learn ", StringComparison.OrdinalIgnoreCase))
        {
            var definition = prompt["/learn ".Length..].Trim();
            var separator = definition.IndexOf("=>", StringComparison.Ordinal);
            if (separator <= 0 || separator + 2 >= definition.Length)
                message = "Usage: /learn <trigger> => <action>";
            else
                message = await _assistant.LearnPracticeAsync(
                    definition[..separator].Trim(),
                    definition[(separator + 2)..].Trim(),
                    cancellationToken);
        }
        else if (prompt.StartsWith("/forget-instinct ", StringComparison.OrdinalIgnoreCase))
        {
            message = await _assistant.ForgetLearnedPracticeAsync(
                prompt["/forget-instinct ".Length..].Trim(),
                cancellationToken);
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
