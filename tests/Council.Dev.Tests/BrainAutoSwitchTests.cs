using Council.AI.Providers;
using Council.App.Chat;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.Dev.Tests;

public sealed class BrainAutoSwitchTests
{
    private sealed class FixedStatus(bool online) : IInternetStatus
    {
        public bool Online { get; set; } = online;
        public bool IsOnline() => Online;
    }

    private sealed class Stub(string name, bool fail = false) : IAIProvider
    {
        public string Name => name;
        public int Calls { get; private set; }

        public Task<AIResponse> AskAsync(AIRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fail) throw new HttpRequestException(name + " unreachable");
            return Task.FromResult(new AIResponse(name, "ok from " + name));
        }
    }

    private static AIRequest Hi() => new([new AIMessage("user", "hi")]);

    [Fact]
    public async Task OnlineUsesTheCloud()
    {
        var cloud = new Stub("cloud");
        var local = new Stub("local");
        var brain = new ConnectivityAwareProvider([cloud], local, new FixedStatus(true));
        Assert.Equal("cloud", (await brain.AskAsync(Hi())).Provider);
        Assert.Equal("cloud", brain.LastRoute);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task OfflineGoesStraightToLocalWithoutTryingTheCloud()
    {
        var cloud = new Stub("cloud");
        var local = new Stub("local");
        var brain = new ConnectivityAwareProvider([cloud], local, new FixedStatus(false));
        Assert.Equal("local", (await brain.AskAsync(Hi())).Provider);
        Assert.Equal(0, cloud.Calls);
        Assert.Equal("local", brain.LastRoute);
    }

    [Fact]
    public async Task CloudFailuresFallBackThroughEveryCloudThenLocal()
    {
        var first = new Stub("nemotron", fail: true);
        var second = new Stub("gemma", fail: true);
        var local = new Stub("local");
        var brain = new ConnectivityAwareProvider([first, second], local, new FixedStatus(true), TimeSpan.Zero);
        Assert.Equal("local", (await brain.AskAsync(Hi())).Provider);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task ReportsEveryFailureWhenNothingAnswers()
    {
        var brain = new ConnectivityAwareProvider([new Stub("cloud", true)], new Stub("local", true), new FixedStatus(true), TimeSpan.Zero);
        var error = await Assert.ThrowsAsync<AggregateException>(() => brain.AskAsync(Hi()));
        Assert.Equal(2, error.InnerExceptions.Count);
    }

    [Fact]
    public async Task UsesTheLargerLocalModelOnlyWhenRamIsFree()
    {
        var large = new Stub("qwen3.5:4b");
        var small = new Stub("qwen3.5:2b");
        long free = 6L << 30;
        var local = new MemoryAwareLocalProvider(large, small, 4_500L << 20, () => free);
        Assert.Equal("qwen3.5:4b", (await local.AskAsync(Hi())).Provider);
        free = 2L << 30;
        Assert.Equal("qwen3.5:2b", (await local.AskAsync(Hi())).Provider);
    }

    [Fact]
    public async Task FallsBackToTheSmallModelWhenTheLargeOneFails()
    {
        var local = new MemoryAwareLocalProvider(new Stub("large", fail: true), new Stub("small"), 1, () => 8L << 30);
        Assert.Equal("small", (await local.AskAsync(Hi())).Provider);
    }

    [Fact]
    public void InternetCheckIsCachedAndSkipsProbesWhenNoNetwork()
    {
        var probes = 0;
        var now = DateTimeOffset.UtcNow;
        var available = true;
        var status = new InternetStatus(TimeSpan.FromSeconds(15), () => available, _ => { probes++; return true; }, () => now);
        Assert.True(status.IsOnline());
        Assert.True(status.IsOnline());
        Assert.Equal(1, probes);

        available = false;
        now = now.AddSeconds(20);
        Assert.False(status.IsOnline());
        Assert.Equal(1, probes);
    }

    [Fact]
    public void ConnectedToWifiButNoInternetCountsAsOffline()
    {
        var status = new InternetStatus(networkAvailable: () => true, probe: _ => false);
        Assert.False(status.IsOnline());
    }

    [Fact]
    public void FactoryWiresCloudSelectionsAsAutoSwitch()
    {
        string? Env(string key) => key switch
        {
            "JARVIS_AI_PROVIDER" => "omniroute",
            "OMNIROUTE_API_KEY" => "test-key",
            "OMNIROUTE_MODEL" => "openrouter/nvidia/nemotron-3-ultra-550b-a55b:free",
            "OMNIROUTE_FREE_MODELS" => "openrouter/nvidia/nemotron-3-ultra-550b-a55b:free",
            _ => null
        };
        var provider = DesktopProviderFactory.Create(Env);
        var auto = Assert.IsType<ConnectivityAwareProvider>(provider);
        Assert.Contains("larger model when RAM allows", auto.Name);

        string? LocalOnly(string key) => key == "JARVIS_AI_PROVIDER" ? "ollama" : null;
        Assert.IsType<MemoryAwareLocalProvider>(DesktopProviderFactory.Create(LocalOnly));

        string? NoLarge(string key) => key switch { "JARVIS_AI_PROVIDER" => "ollama", "OLLAMA_LARGE_MODEL" => "off", _ => null };
        Assert.IsType<OllamaProvider>(DesktopProviderFactory.Create(NoLarge));
    }
}
