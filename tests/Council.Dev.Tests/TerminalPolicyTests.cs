using Council.Dev.Terminal;

namespace Council.Dev.Tests;

public class TerminalPolicyTests
{
    [Fact]
    public async Task Blocked_command_never_executes()
    {
        var controller = new TerminalController();

        var command = new TerminalCommand(
            "Remove-Item definitely-not-a-real-file.txt",
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => controller.ExecuteAsync(command));

        Assert.Contains(
            "Command blocked by policy",
            exception.Message);
    }
}
