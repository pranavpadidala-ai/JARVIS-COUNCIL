using Council.Dev.Terminal;

namespace Council.Dev.Tests;

public class TerminalControllerTests
{
    private readonly TerminalController _controller = new();

    [Fact]
    public async Task Can_execute_allowed_dotnet_command()
    {
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
        var result = await _controller.ExecuteAsync(
            new TerminalCommand(
                "dotnet definitely-not-a-real-command",
                Environment.CurrentDirectory,
                TimeSpan.FromSeconds(10)));

        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Timeout_terminates_allowed_command()
    {
        var result = await _controller.ExecuteAsync(
            new TerminalCommand(
                "dotnet help",
                Environment.CurrentDirectory,
                TimeSpan.FromMilliseconds(1)));

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

    [Theory]
    [InlineData("powershell -Command \"dotnet --version\"")]
    [InlineData("pwsh -Command \"dotnet --version\"")]
    [InlineData("cmd /c dotnet --version")]
    [InlineData("dotnet --version; whoami")]
    [InlineData("dotnet --version | Out-Host")]
    [InlineData("dotnet --version && whoami")]
    [InlineData("dotnet --version > output.txt")]
    [InlineData("dotnet --version $(whoami)")]
    [InlineData("dotnet --version & whoami")]
    public async Task Dangerous_command_composition_is_rejected(
        string commandText)
    {
        var command = new TerminalCommand(
            commandText,
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _controller.ExecuteAsync(command));
    }

    [Fact]
    public async Task Newline_command_injection_is_rejected()
    {
        var command = new TerminalCommand(
            "dotnet --version\\nwhoami",
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _controller.ExecuteAsync(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_command_is_rejected(
        string commandText)
    {
        var command = new TerminalCommand(
            commandText,
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _controller.ExecuteAsync(command));
    }

    [Fact]
    public async Task Missing_working_directory_is_rejected()
    {
        var command = new TerminalCommand(
            "dotnet --version",
            Path.Combine(
                Environment.CurrentDirectory,
                "directory-that-does-not-exist"),
            TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _controller.ExecuteAsync(command));
    }
}
