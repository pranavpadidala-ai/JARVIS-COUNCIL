using Council.App;
using Council.App.Safety;
using Council.Execution.Tools;
using Xunit;

namespace Council.Dev.Tests;

public sealed class EmergencyStopTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), "jarvis-actions-" + Guid.NewGuid().ToString("N") + ".jsonl");
    private readonly ActionLog _log;

    public EmergencyStopTests() => _log = new ActionLog(_logPath);

    public void Dispose()
    {
        if (File.Exists(_logPath)) File.Delete(_logPath);
    }

    [Fact]
    public async Task HaltedSwitchBlocksToolsWithoutRunningThem()
    {
        var stop = new EmergencyStopSwitch();
        var tool = new CountingTool();
        stop.Halt("test");

        var result = await ToolCallingAssistant.RunToolAsync(tool, new(), CancellationToken.None, stop, _log);

        Assert.False(result.Success);
        Assert.Contains("emergency stop", result.Message);
        Assert.Equal(0, tool.Runs);
    }

    [Fact]
    public async Task HaltCancelsARunningToolAndResumeAllowsNewOnes()
    {
        var stop = new EmergencyStopSwitch();
        var slow = new SlowTool();
        var running = ToolCallingAssistant.RunToolAsync(slow, new(), CancellationToken.None, stop, _log);
        await slow.Started.Task;

        stop.Halt("test");
        var stopped = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopped.Success);
        Assert.Contains("Stopped by emergency stop", stopped.Message);

        stop.Resume();
        var tool = new CountingTool();
        var after = await ToolCallingAssistant.RunToolAsync(tool, new(), CancellationToken.None, stop, _log);
        Assert.True(after.Success);
        Assert.Equal(1, tool.Runs);
    }

    [Fact]
    public void ChangedFiresOncePerTransition()
    {
        var stop = new EmergencyStopSwitch();
        var changes = 0;
        stop.Changed += (_, _) => changes++;
        stop.Halt("a");
        stop.Halt("b"); // already halted
        Assert.Equal("a", stop.Reason);
        stop.Resume();
        stop.Resume(); // already running
        Assert.Equal(2, changes);
        Assert.False(stop.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task UserCancellationIsNotReportedAsEmergencyStop()
    {
        var stop = new EmergencyStopSwitch();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ToolCallingAssistant.RunToolAsync(new SlowTool(), new(), cts.Token, stop, _log));
    }

    private sealed class CountingTool : ITool
    {
        public int Runs { get; private set; }
        public string Name => "counting";
        public string Description => "test";
        public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
        {
            Runs++;
            return Task.FromResult(new ToolResult(true, "ran"));
        }
    }

    private sealed class SlowTool : ITool
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => "slow";
        public string Description => "test";
        public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ToolResult(true, "never");
        }
    }
}
