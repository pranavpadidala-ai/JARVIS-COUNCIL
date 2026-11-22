using Council.AI.Providers;
using Council.Core.Providers;

namespace Council.App.Chat;

/// <summary>
/// Builds the chat provider for the desktop and console assistant.
/// The selected provider is tried first. Unless JARVIS_PROVIDER_FALLBACK is "off",
/// other providers that are already configured are added behind it, so a stopped
/// Ollama service or a cloud quota error does not take JARVIS offline:
///   local Ollama selected  -> Gemini (if GEMINI_API_KEY) -> OpenAI (if OPENAI_API_KEY)
///   a cloud provider       -> the other cloud provider (if keyed) -> local Ollama
/// Claude is paid per use, so it is never added as a fallback. It runs only when
/// JARVIS_AI_PROVIDER=claude is chosen on purpose, and then falls back to the others.
/// With nothing selected, JARVIS starts local-first with Ollama.
/// Mock mode never falls back.
/// </summary>
public static class DesktopProviderFactory
{
    public const string DefaultOllamaModel = "qwen3.5:2b-q4_K_M";
    public const string DefaultGeminiModel = "gemini-3.6-flash";
    public const string DefaultOpenAIModel = "gpt-5.2";
    public const string DefaultClaudeModel = ClaudeProvider.DefaultModel;

    public const string DefaultOllamaLargeModel = "qwen3.5:4b";
    private const long DefaultLargeModelMinFreeBytes = 4_500L * 1024 * 1024;

    /// <summary>
    /// The chain from <see cref="CreateChain"/>, wired the owner's way:
    /// a cloud provider is selected -> online: cloud providers, then local; offline: local immediately.
    /// The local brain uses the larger model (OLLAMA_LARGE_MODEL, default qwen3.5:4b) when enough RAM is free,
    /// otherwise OLLAMA_MODEL. JARVIS_PROVIDER_FALLBACK=off keeps exactly the selected provider.
    /// </summary>
    public static IAIProvider Create(Func<string, string?>? environment = null)
    {
        var env = environment ?? Environment.GetEnvironmentVariable;
        var providers = CreateChain(env);
        var fallbackEnabled = !string.Equals(env("JARVIS_PROVIDER_FALLBACK")?.Trim(), "off", StringComparison.OrdinalIgnoreCase);
        if (!fallbackEnabled || providers.Count == 1 && providers[0] is not OllamaProvider)
            return providers.Count == 1 ? providers[0] : new FallbackAIProvider(providers);

        var wired = providers.Select(p => p is OllamaProvider ? WithLargeModel(p, env) : p).ToList();
        if (wired.Count == 1) return wired[0];

        var cloudFirst = providers[0] is not OllamaProvider && providers[^1] is OllamaProvider;
        return cloudFirst
            ? new ConnectivityAwareProvider(wired.Take(wired.Count - 1), wired[^1])
            : new FallbackAIProvider(wired);
    }

    private static IAIProvider WithLargeModel(IAIProvider small, Func<string, string?> env)
    {
        var smallModel = NonEmpty(env("OLLAMA_MODEL")) ?? DefaultOllamaModel;
        var largeModel = NonEmpty(env("OLLAMA_LARGE_MODEL")) ?? DefaultOllamaLargeModel;
        if (largeModel.Equals("off", StringComparison.OrdinalIgnoreCase) || largeModel.Equals(smallModel, StringComparison.OrdinalIgnoreCase))
            return small;
        var minFree = long.TryParse(env("JARVIS_OLLAMA_LARGE_MIN_FREE_MB"), out var mb) && mb > 0 ? mb * 1024 * 1024 : DefaultLargeModelMinFreeBytes;
        return new MemoryAwareLocalProvider(CreateOllama(env, largeModel), small, minFree);
    }

    public static IReadOnlyList<IAIProvider> CreateChain(Func<string, string?>? environment = null)
    {
        var env = environment ?? Environment.GetEnvironmentVariable;
        var selection = env("JARVIS_AI_PROVIDER")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(selection))
            selection = "ollama";

        var geminiKey = env("GEMINI_API_KEY");
        var openAiKey = env("OPENAI_API_KEY");
        var claudeKey = env("ANTHROPIC_API_KEY");
        var fallbackEnabled = !string.Equals(env("JARVIS_PROVIDER_FALLBACK")?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

        var chain = new List<IAIProvider>();
        switch (selection)
        {
            case "mock":
                return new IAIProvider[] { new MockAIProvider() };

            case "ollama":
                chain.Add(CreateOllama(env));
                if (fallbackEnabled)
                {
                    AddGemini(chain, env, geminiKey);
                    AddOpenAI(chain, env, openAiKey);
                }
                break;

            case "gemini":
                if (string.IsNullOrWhiteSpace(geminiKey))
                    throw new InvalidOperationException("GEMINI_API_KEY is required when JARVIS_AI_PROVIDER=gemini.");
                AddGemini(chain, env, geminiKey);
                if (fallbackEnabled)
                {
                    AddOpenAI(chain, env, openAiKey);
                    chain.Add(CreateOllama(env));
                }
                break;

            case "openai":
                if (string.IsNullOrWhiteSpace(openAiKey))
                    throw new InvalidOperationException("OPENAI_API_KEY is required when JARVIS_AI_PROVIDER=openai.");
                AddOpenAI(chain, env, openAiKey);
                if (fallbackEnabled)
                {
                    AddGemini(chain, env, geminiKey);
                    chain.Add(CreateOllama(env));
                }
                break;

            case "claude":
                if (string.IsNullOrWhiteSpace(claudeKey))
                    throw new InvalidOperationException("ANTHROPIC_API_KEY is required when JARVIS_AI_PROVIDER=claude.");
                AddClaude(chain, env, claudeKey);
                if (fallbackEnabled)
                {
                    AddGemini(chain, env, geminiKey);
                    AddOpenAI(chain, env, openAiKey);
                    chain.Add(CreateOllama(env));
                }
                break;

            case "omniroute":
                var omniKey = env("OMNIROUTE_API_KEY");
                var omniModel = env("OMNIROUTE_MODEL");
                var omniBaseUrl = env("OMNIROUTE_BASE_URL") ?? "http://localhost:20128/v1";
                var allowedModels = env("OMNIROUTE_FREE_MODELS");
                if (string.IsNullOrWhiteSpace(omniKey))
                    throw new InvalidOperationException("OMNIROUTE_API_KEY is required when JARVIS_AI_PROVIDER=omniroute.");
                if (string.IsNullOrWhiteSpace(omniModel) || string.IsNullOrWhiteSpace(allowedModels))
                    throw new InvalidOperationException("Set OMNIROUTE_MODEL and OMNIROUTE_FREE_MODELS to exact approved model IDs. Automatic paid routing is disabled.");
                chain.Add(new OmniRouteProvider(omniKey, omniModel, omniBaseUrl, allowedModels));
                if (fallbackEnabled)
                    chain.Add(CreateOllama(env));
                break;

            default:
                throw new InvalidOperationException($"Unknown JARVIS_AI_PROVIDER '{selection}'. Choose ollama, gemini, openai, claude, omniroute or mock.");
        }

        return chain;
    }

    private static IAIProvider CreateOllama(Func<string, string?> env, string? modelOverride = null)
    {
        var model = modelOverride ?? NonEmpty(env("OLLAMA_MODEL")) ?? DefaultOllamaModel;
        var baseUrl = NonEmpty(env("OLLAMA_BASE_URL")) ?? "http://localhost:11434";
        var contextTokens = int.TryParse(env("JARVIS_OLLAMA_CONTEXT_TOKENS"), out var configuredContext)
            ? Math.Clamp(configuredContext, 2048, 8192)
            : 4096;
        return new OllamaProvider(model, baseUrl, contextTokens);
    }

    private static void AddGemini(List<IAIProvider> chain, Func<string, string?> env, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            chain.Add(new GeminiProvider(key, NonEmpty(env("GEMINI_MODEL")) ?? DefaultGeminiModel));
    }

    private static void AddOpenAI(List<IAIProvider> chain, Func<string, string?> env, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            chain.Add(new OpenAIProvider(key, NonEmpty(env("OPENAI_MODEL")) ?? DefaultOpenAIModel));
    }

    private static void AddClaude(List<IAIProvider> chain, Func<string, string?> env, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            chain.Add(new ClaudeProvider(key, NonEmpty(env("CLAUDE_MODEL")) ?? DefaultClaudeModel));
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
