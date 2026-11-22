using Council.App.Devices;
using Xunit;

namespace Council.Dev.Tests;

public sealed class TvRemoteToolTests : IDisposable
{
    private readonly string _hostFile = Path.Combine(Path.GetTempPath(), "jarvis-tv-" + Guid.NewGuid().ToString("N") + ".txt");
    private readonly List<string[]> _calls = new();

    public void Dispose()
    {
        if (File.Exists(_hostFile)) File.Delete(_hostFile);
    }

    private TvRemoteTool Tool(string reply = "{\"ok\": true}") =>
        new((args, _) => { _calls.Add(args.ToArray()); return Task.FromResult(reply); }, _hostFile);

    [Fact]
    public async Task MapsFriendlyKeysAndApps()
    {
        var tool = Tool();
        Assert.True((await tool.ExecuteAsync(new() { ["action"] = "key", ["key"] = "volume up", ["host"] = "192.168.29.50" })).Success);
        Assert.True((await tool.ExecuteAsync(new() { ["action"] = "open_app", ["app"] = "YouTube", ["host"] = "192.168.29.50" })).Success);
        Assert.Equal(new[] { "key", "192.168.29.50", "VOLUME_UP" }, _calls[0]);
        Assert.Equal(new[] { "launch", "192.168.29.50", "https://www.youtube.com" }, _calls[1]);
    }

    [Fact]
    public async Task OnlyTalksToHomeNetworkAddresses()
    {
        var tool = Tool();
        Assert.False((await tool.ExecuteAsync(new() { ["action"] = "key", ["key"] = "power", ["host"] = "8.8.8.8" })).Success);
        Assert.False((await tool.ExecuteAsync(new() { ["action"] = "key", ["key"] = "power" })).Success); // no host yet
        Assert.Empty(_calls);
        Assert.True(TvRemoteTool.IsPrivateAddress("10.0.0.7"));
        Assert.False(TvRemoteTool.IsPrivateAddress("tv.example.com"));
    }

    [Fact]
    public async Task UsesTheSavedHostAndReportsDiscovery()
    {
        await File.WriteAllTextAsync(_hostFile, "192.168.1.40");
        var tool = Tool("{\"ok\": true, \"devices\": [{\"name\": \"JioSTB\", \"host\": \"192.168.1.40\"}]}");
        var found = await tool.ExecuteAsync(new() { ["action"] = "discover" });
        Assert.Contains("JioSTB at 192.168.1.40", found.Message);
        await tool.ExecuteAsync(new() { ["action"] = "key", ["key"] = "home" });
        Assert.Equal("192.168.1.40", _calls[^1][1]);

        var failing = Tool("{\"ok\": false, \"error\": \"Not paired with this TV yet.\"}");
        Assert.Contains("Not paired", (await failing.ExecuteAsync(new() { ["action"] = "status" })).Message);
    }
}
