using Council.Dev.Terminal;

namespace Council.Dev.Tests;

public class CommandPolicyTests
{
    private readonly CommandPolicy _policy = new();

    [Theory]
    [InlineData("dotnet build")]
    [InlineData("dotnet test")]
    [InlineData("dotnet restore")]
    [InlineData("dotnet run")]
    [InlineData("git status")]
    [InlineData("git diff")]
    public void Development_commands_are_allowed(string command)
    {
        Assert.True(_policy.IsAllowed(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Remove-Item test.txt")]
    [InlineData("shutdown /s /t 0")]
    [InlineData("format C:")]
    [InlineData("reg delete HKCU")]
    [InlineData("powershell -Command Get-Process")]
    public void Dangerous_or_unapproved_commands_are_blocked(string command)
    {
        Assert.False(_policy.IsAllowed(command));
    }
}
