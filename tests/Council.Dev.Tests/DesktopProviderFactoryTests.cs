using Council.AI.Providers;
using Council.App.Chat;

namespace Council.Dev.Tests;

public sealed class DesktopProviderFactoryTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        return key => map.TryGetValue(key, out var value) ? value : null;
    }

    [Fact]
    public void NoSelectionStartsLocalFirstWithOllamaInsteadOfMock()
    {
        var chain = DesktopProviderFactory.CreateChain(Env());

        var only = Assert.Single(chain);
        Assert.IsType<OllamaProvider>(only);
    }

    [Fact]
    public void OllamaFallsBackToConfiguredCloudProvidersInOrder()
    {
        var chain = DesktopProviderFactory.CreateChain(Env(
            ("JARVIS_AI_PROVIDER", "ollama"),
            ("GEMINI_API_KEY", "test-gemini"),
            ("OPENAI_API_KEY", "test-openai")));

        Assert.Collection(chain,
            p => Assert.IsType<OllamaProvider>(p),
            p => Assert.IsType<GeminiProvider>(p),
            p => Assert.IsType<OpenAIProvider>(p));
        Assert.IsType<FallbackAIProvider>(DesktopProviderFactory.Create(Env(
            ("JARVIS_AI_PROVIDER", "ollama"), ("GEMINI_API_KEY", "test-gemini"))));
    }

    [Fact]
    public void CloudProviderFallsBackToLocalOllama()
    {
        var chain = DesktopProviderFactory.CreateChain(Env(
            ("JARVIS_AI_PROVIDER", "gemini"),
            ("GEMINI_API_KEY", "test-gemini")));

        Assert.Collection(chain,
            p => Assert.IsType<GeminiProvider>(p),
            p => Assert.IsType<OllamaProvider>(p));
    }

    [Fact]
    public void FallbackCanBeTurnedOff()
    {
        var provider = DesktopProviderFactory.Create(Env(
            ("JARVIS_AI_PROVIDER", "ollama"),
            ("GEMINI_API_KEY", "test-gemini"),
            ("JARVIS_PROVIDER_FALLBACK", "off")));

        Assert.IsType<OllamaProvider>(provider);
    }

    [Fact]
    public void MockModeNeverFallsBack()
    {
        var provider = DesktopProviderFactory.Create(Env(
            ("JARVIS_AI_PROVIDER", "mock"),
            ("GEMINI_API_KEY", "test-gemini")));

        Assert.IsType<MockAIProvider>(provider);
    }

    [Fact]
    public void SelectedCloudProviderStillRequiresItsKey()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DesktopProviderFactory.CreateChain(Env(("JARVIS_AI_PROVIDER", "openai"))));
    }
}
