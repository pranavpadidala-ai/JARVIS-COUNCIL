using Council.App.Windows;

namespace Council.Dev.Tests;

public sealed class DesktopControlToolTests
{
    [Fact]
    public async Task LaunchApp_UsesOnlyAllowlistedExecutable()
    {
        string? launched = null;
        var tool = new DesktopControlTool(executable =>
        {
            launched = executable;
            return true;
        });

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "launch_app",
            ["app"] = "calculator"
        });

        Assert.True(result.Success);
        Assert.Equal("calc.exe", launched);
    }

    [Fact]
    public async Task LaunchApp_RejectsArbitraryExecutablePath()
    {
        var launchAttempted = false;
        var tool = new DesktopControlTool(_ =>
        {
            launchAttempted = true;
            return true;
        });

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "launch_app",
            ["app"] = @"C:\\Windows\\System32\\cmd.exe"
        });

        Assert.False(result.Success);
        Assert.False(launchAttempted);
    }

    [Fact]
    public async Task LaunchApp_HonorsCancellationBeforeStarting()
    {
        var launchAttempted = false;
        var tool = new DesktopControlTool(_ =>
        {
            launchAttempted = true;
            return true;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "launch_app",
                ["app"] = "notepad"
            }, cancellation.Token));

        Assert.False(launchAttempted);
    }

    [Fact]
    public async Task LaunchApp_RejectsAdditionalArguments()
    {
        var launchAttempted = false;
        var tool = new DesktopControlTool(_ =>
        {
            launchAttempted = true;
            return true;
        });

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "launch_app",
            ["app"] = "notepad",
            ["arguments"] = "anything"
        });

        Assert.False(result.Success);
        Assert.False(launchAttempted);
    }
}
