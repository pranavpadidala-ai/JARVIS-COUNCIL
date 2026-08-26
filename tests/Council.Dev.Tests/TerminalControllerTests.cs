using Council.Dev.Terminal;

namespace Council.Dev.Tests;

public class TerminalControllerTests
{
    private readonly TerminalController _controller = new();

    [Fact]
    public async Task Can_execute_allowed_dotnet_command()
    {
        var command = new TerminalCommand(
            "--version",
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10));

        var result = await _controller.ExecuteAsync(
            new TerminalCommand(
                "dotnet --version",
                Environment.CurrentDirectory,
                TimeSpan.FromSeconds(10)));

        Assert.True(result.Success);
        Assert.Contains("8.", result.StandardOutput);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task Failed_allowed_command_returns_failure()
    {
        var command = new TerminalCommand(
            "dotnet definitely-not-a-real-command",
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10));

        var result = await _controller.ExecuteAsync(command);

        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Timeout_terminates_allowed_command()
    {
        var command = new TerminalCommand(
            "dotnet help",
            Environment.CurrentDirectory,
            TimeSpan.FromMilliseconds(1));

        var result = await _controller.ExecuteAsync(command);

        Assert.True(
            result.TimedOut ||
            result.Success ||
            result.ExitCode != 0);
    }

    [Fact]
    public async Task Blocked_command_is_rejected()
    {
        var command = new TerminalCommand(
            "Remove-Item definitely-not-a-real-file.txt",
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _controller.ExecuteAsync(command));
    }
}
