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
    [InlineData("dotnet --version")]
    [InlineData("git status")]
    [InlineData("git diff")]
    [InlineData("git log --oneline -5")]
    [InlineData("git rev-parse HEAD")]
    public void Development_commands_are_allowed(string command)
    {
        Assert.True(_policy.IsAllowed(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Remove-Item test.txt")]
    [InlineData("Add-Content test.txt hello")]
    [InlineData("Set-Content test.txt hello")]
    [InlineData("shutdown /s /t 0")]
    [InlineData("format C:")]
    [InlineData("reg delete HKCU")]
    [InlineData("powershell -Command Get-Process")]
    [InlineData("pwsh -Command Get-Process")]
    [InlineData("cmd /c whoami")]
    [InlineData("Invoke-Expression Get-Process")]
    [InlineData("Invoke-Command Get-Process")]
    [InlineData("Start-Process powershell")]
    [InlineData("Stop-Process -Name dotnet")]
    [InlineData("Invoke-WebRequest example.com")]
    [InlineData("Invoke-RestMethod example.com")]
    [InlineData("Set-ExecutionPolicy Unrestricted")]
    public void Dangerous_or_unapproved_commands_are_blocked(string command)
    {
        Assert.False(_policy.IsAllowed(command));
    }

    [Theory]
    [InlineData("dotnet build; whoami")]
    [InlineData("dotnet build && whoami")]
    [InlineData("dotnet build || whoami")]
    [InlineData("dotnet build | Out-Host")]
    [InlineData("dotnet build > output.txt")]
    [InlineData("dotnet build >> output.txt")]
    [InlineData("dotnet build < input.txt")]
    [InlineData("dotnet build `whoami`")]
    [InlineData("dotnet build $(whoami)")]
    [InlineData("dotnet build & whoami")]
    [InlineData("git status; Remove-Item test.txt")]
    [InlineData("git status | Out-Host")]
    [InlineData("dotnet build\nwhoami")]
    [InlineData("dotnet test\r\nRemove-Item test.txt")]
    [InlineData("git status\nStart-Process powershell")]
    [InlineData("dotnet\tbuild")]
    public void Command_composition_is_blocked(string command)
    {
        Assert.False(_policy.IsAllowed(command));
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("python")]
    [InlineData("node")]
    [InlineData("npm install")]
    [InlineData("curl example.com")]
    [InlineData("whoami")]
    [InlineData("ipconfig")]
    public void Unapproved_executables_are_blocked(string command)
    {
        Assert.False(_policy.IsAllowed(command));
    }

    [Theory]
    [InlineData("DOTNET build")]
    [InlineData("DotNet test")]
    [InlineData("GIT status")]
    [InlineData("Git diff")]
    public void Approved_executables_are_case_insensitive(string command)
    {
        Assert.True(_policy.IsAllowed(command));
    }
}
