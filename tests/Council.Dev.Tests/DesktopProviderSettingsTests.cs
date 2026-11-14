using Council.App.Chat;

namespace Council.Dev.Tests;

[CollectionDefinition("Desktop provider settings environment", DisableParallelization = true)]
public sealed class DesktopProviderSettingsEnvironmentCollection;

[Collection("Desktop provider settings environment")]
public sealed class DesktopProviderSettingsTests
{
    [Fact]
    public void SaveAndLoadRestoresProviderAndModelWithoutPersistingCredentials()
    {
        using var workspace = new TemporaryWorkspace();
        using var environment = new EnvironmentSnapshot(
            "JARVIS_AI_PROVIDER", "GEMINI_MODEL", "GEMINI_API_KEY");
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-secret-do-not-persist");

        var path = Path.Combine(workspace.Path, "desktop-settings.json");
        DesktopProviderSettingsStore.SaveAndApply("gemini", "gemini-3.6-flash", path);

        Assert.Equal("gemini", Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER"));
        Assert.Equal("gemini-3.6-flash", Environment.GetEnvironmentVariable("GEMINI_MODEL"));
        var json = File.ReadAllText(path);
        Assert.Contains("gemini-3.6-flash", json);
        Assert.DoesNotContain("test-secret-do-not-persist", json);
        Assert.DoesNotContain("GEMINI_API_KEY", json);

        Environment.SetEnvironmentVariable("JARVIS_AI_PROVIDER", "mock");
        Environment.SetEnvironmentVariable("GEMINI_MODEL", "old-model");
        var loaded = DesktopProviderSettingsStore.TryLoadAndApplySaved(out var message, path);

        Assert.NotNull(loaded);
        Assert.Equal("gemini", loaded.Provider);
        Assert.Equal("gemini-3.6-flash", loaded.Model);
        Assert.Equal("Saved provider settings loaded.", message);
        Assert.Equal("gemini", Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER"));
        Assert.Equal("gemini-3.6-flash", Environment.GetEnvironmentVariable("GEMINI_MODEL"));
        Assert.Equal("test-secret-do-not-persist", Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
    }

    [Fact]
    public void MissingSettingsKeepsCurrentEnvironmentSelection()
    {
        using var workspace = new TemporaryWorkspace();
        using var environment = new EnvironmentSnapshot("JARVIS_AI_PROVIDER", "OLLAMA_MODEL");
        Environment.SetEnvironmentVariable("JARVIS_AI_PROVIDER", "ollama");
        Environment.SetEnvironmentVariable("OLLAMA_MODEL", "qwen3.5:2b-q4_K_M");

        var loaded = DesktopProviderSettingsStore.TryLoadAndApplySaved(
            out var message, Path.Combine(workspace.Path, "missing.json"));

        Assert.Null(loaded);
        Assert.Contains("No saved", message);
        Assert.Equal("ollama", Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER"));
        Assert.Equal("qwen3.5:2b-q4_K_M", Environment.GetEnvironmentVariable("OLLAMA_MODEL"));
    }

    [Theory]
    [InlineData("unknown", "model")]
    [InlineData("ollama", "")]
    [InlineData("gemini", "model with spaces")]
    [InlineData("openai", "model\nline-break")]
    public void InvalidProviderOrModelIsRejected(string provider, string model)
    {
        using var workspace = new TemporaryWorkspace();
        var path = Path.Combine(workspace.Path, "desktop-settings.json");

        Assert.Throws<ArgumentException>(() =>
            DesktopProviderSettingsStore.SaveAndApply(provider, model, path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void MockSelectionDoesNotNeedOrStoreAModel()
    {
        using var workspace = new TemporaryWorkspace();
        using var environment = new EnvironmentSnapshot("JARVIS_AI_PROVIDER", "OLLAMA_MODEL");
        var path = Path.Combine(workspace.Path, "desktop-settings.json");

        DesktopProviderSettingsStore.SaveAndApply("mock", "ignored-model", path);
        var current = DesktopProviderSettingsStore.GetCurrent();

        Assert.Equal("mock", current.Provider);
        Assert.Equal(string.Empty, current.Model);
        Assert.Equal("mock", Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER"));
        Assert.DoesNotContain("OLLAMA_MODEL", File.ReadAllText(path));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "JARVIS-DesktopSettings", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class EnvironmentSnapshot(params string[] names) : IDisposable
    {
        private readonly Dictionary<string, string?> _values = names.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable);

        public void Dispose()
        {
            foreach (var pair in _values)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
