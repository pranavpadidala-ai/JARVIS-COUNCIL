using Council.App.Browser;
using Council.Execution.Tools;

namespace Council.Dev.Tests;

public sealed class BrowserAutomationToolTests
{
    [Fact]
    public async Task BeginStartsDedicatedSessionAndInspectUsesIt()
    {
        var runtime = new FakeRuntime();
        var root = Path.Combine(Path.GetTempPath(), "JarvisBrowserTests", Guid.NewGuid().ToString("N"));
        var tool = new BrowserAutomationTool(root, runtime);
        try
        {
            var begin = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "begin",
                ["purpose"] = "Research a public engineering standard"
            });
            Assert.True(begin.Success, begin.Message);
            Assert.True(runtime.ProfileDirectory.Contains(Path.Combine("UserData", "BrowserProfiles", "Brave"), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, runtime.LaunchCount);

            var inspect = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "inspect" });
            Assert.True(inspect.Success, inspect.Message);
            Assert.Contains("FAKE PAGE", inspect.Message);
        }
        finally
        {
            await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "end" });
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task NavigationBlocksJavascriptAndFileUrlsBeforeRuntimeCall()
    {
        var runtime = new FakeRuntime();
        var root = Path.Combine(Path.GetTempPath(), "JarvisBrowserTests", Guid.NewGuid().ToString("N"));
        var tool = new BrowserAutomationTool(root, runtime);
        try
        {
            var begin = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "begin", ["purpose"] = "Check a public web page"
            });
            Assert.True(begin.Success, begin.Message);
            var javascript = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "navigate", ["url"] = "javascript:alert(1)"
            });
            var file = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "navigate", ["url"] = "file:///C:/Windows/win.ini"
            });
            Assert.False(javascript.Success);
            Assert.False(file.Success);
            Assert.Equal(0, runtime.Session.NavigateCount);
        }
        finally
        {
            await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "end" });
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SessionIsRequiredAndBeginRejectsUnboundedArguments()
    {
        var tool = new BrowserAutomationTool(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), new FakeRuntime());
        var noSession = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "inspect" });
        var missingPurpose = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "begin" });
        var extraArgument = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "begin", ["purpose"] = "Browse safely", ["profile"] = "C:\\Users\\Default"
        });
        Assert.False(noSession.Success);
        Assert.False(missingPurpose.Success);
        Assert.False(extraArgument.Success);
    }

    [Fact]
    public async Task ArbitraryButtonClickAndFormSubmitAreNotSupported()
    {
        var tool = new BrowserAutomationTool(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), new FakeRuntime());
        var click = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "click", ["selector"] = "button#submit"
        });
        var submit = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "submit"
        });
        Assert.False(click.Success);
        Assert.False(submit.Success);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hello", false)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("https://example.com/?access_token=abc", false)]
    public void UrlPolicyAllowsOnlyOrdinaryWebUrls(string url, bool expected)
    {
        Assert.Equal(expected, BrowserAutomationPolicy.IsSafeHttpUrl(url, out _, out _));
    }

    [Theory]
    [InlineData("input", "text", "", "search", "q", "Search", "", true)]
    [InlineData("textarea", "", "", "message", "message", "Message", "", true)]
    [InlineData("input", "password", "current-password", "password", "pwd", "Password", "", false)]
    [InlineData("input", "text", "one-time-code", "otp", "otp", "Code", "", false)]
    [InlineData("input", "text", "cc-number", "card", "card", "Card number", "", false)]
    [InlineData("input", "file", "", "upload", "upload", "Choose file", "", false)]
    [InlineData("button", "", "", "submit", "submit", "Submit", "", false)]
    public void TextFieldPolicyRejectsCredentialsPaymentsAndNonTextControls(
        string tag, string type, string autocomplete, string name, string id, string placeholder, string aria, bool expected)
    {
        Assert.Equal(expected, BrowserAutomationPolicy.IsSafeTextField(tag, type, autocomplete, name, id, placeholder, aria));
    }

    private sealed class FakeRuntime : IBrowserAutomationRuntime
    {
        public int LaunchCount { get; private set; }
        public string ProfileDirectory { get; private set; } = string.Empty;
        public FakeSession Session { get; } = new();

        public Task<IBrowserAutomationSession> LaunchAsync(string profileDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCount++;
            ProfileDirectory = profileDirectory;
            return Task.FromResult<IBrowserAutomationSession>(Session);
        }
    }

    private sealed class FakeSession : IBrowserAutomationSession
    {
        public int NavigateCount { get; private set; }
        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NavigateCount++;
            return Task.FromResult("FAKE PAGE: " + url);
        }
        public Task<string> InspectAsync(CancellationToken cancellationToken) => Task.FromResult("FAKE PAGE: test content");
        public Task<string> ClickLinkAsync(string selector, CancellationToken cancellationToken) => Task.FromResult("FAKE PAGE: link clicked");
        public Task<string> FillTextAsync(string selector, string value, CancellationToken cancellationToken) => Task.FromResult("FAKE PAGE: field filled");
        public Task<string> ListTabsAsync(CancellationToken cancellationToken) => Task.FromResult("[0] fake tab");
        public Task<string> SwitchTabAsync(int index, CancellationToken cancellationToken) => Task.FromResult("FAKE PAGE: tab switched");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}