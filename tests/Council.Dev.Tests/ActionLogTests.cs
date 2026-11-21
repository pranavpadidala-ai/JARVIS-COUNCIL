using Council.App;
using Council.App.Safety;
using Council.Execution.Tools;
using Xunit;

namespace Council.Dev.Tests;

public sealed class ActionLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-actionlog-" + Guid.NewGuid().ToString("N") + ".jsonl");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task EveryToolRunIsLoggedNewestFirst()
    {
        var log = new ActionLog(_path);
        var stop = new EmergencyStopSwitch();
        await ToolCallingAssistant.RunToolAsync(new FixedTool("first", true), new() { ["path"] = "notes.txt" }, CancellationToken.None, stop, log);
        await ToolCallingAssistant.RunToolAsync(new FixedTool("second", false), new(), CancellationToken.None, stop, log);

        var entries = log.Recent(10);
        Assert.Equal(2, entries.Count);
        Assert.Equal("second", entries[0].Tool);
        Assert.False(entries[0].Success);
        Assert.Equal("path=notes.txt", entries[1].Arguments);
        Assert.Contains("first", log.Describe());
    }

    [Fact]
    public void SecretsAreRedactedAndLongValuesShortened()
    {
        var log = new ActionLog(_path);
        log.Record("web_open", new Dictionary<string, string>
        {
            ["header"] = "Authorization: Bearer sk-abcdefghijklmnopqrstuvwxyz0123456789",
            ["text"] = new string('x', 1000)
        }, true, "done");

        var raw = File.ReadAllText(_path);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz0123456789", raw);
        Assert.True(log.Recent(1)[0].Arguments.Length < 400);
    }

    [Fact]
    public void EmptyLogSaysSo() =>
        Assert.Equal("JARVIS has not run any tools yet.", new ActionLog(_path).Describe());

    private sealed class FixedTool(string name, bool success) : ITool
    {
        public string Name => name;
        public string Description => "test";
        public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolResult(success, name + " result"));
    }
}
