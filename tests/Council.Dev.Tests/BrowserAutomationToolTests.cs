using Council.App;
using Council.Dev.Browser;

namespace Council.Dev.Tests;

public sealed class BrowserAutomationToolTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "jarvis-browser-tool-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NavigationRejectsNonWebSchemesWithoutLaunchingOrConnecting()
    {
        var root = NewRoot();
        try
        {
            using var manager = new BraveProfileManager(root);
            var profile = await manager.CreateProfileAsync("Test", "Browser automation validation");
            var tool = new BrowserAutomationTool(manager);

            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "navigate",
                ["profile_id"] = profile.Id,
                ["url"] = "file:///C:/Windows/win.ini"
            });

            Assert.False(result.Success);
            Assert.Contains("public absolute HTTP/HTTPS URL", result.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("https://example.com/callback?access_token=abc123")]
    [InlineData("https://example.com/login?password=hunter2")]
    [InlineData("https://example.com/verify?otp=123456&x=1")]
    public async Task NavigationRejectsUrlsCarryingSecretsWithoutConnecting(string url)
    {
        var root = NewRoot();
        try
        {
            using var manager = new BraveProfileManager(root);
            var profile = await manager.CreateProfileAsync("Test", "Browser automation validation");
            var tool = new BrowserAutomationTool(manager);

            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "navigate",
                ["profile_id"] = profile.Id,
                ["url"] = url
            });

            Assert.False(result.Success);
            Assert.Contains("public absolute HTTP/HTTPS URL", result.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ReportedUrlsOmitQueryStringsAndFragments()
    {
        Assert.Equal("https://example.com/path", BrowserAutomationTool.SanitizeUrl("https://user:pw@example.com/path?session=1#frag"));
    }

    [Fact]
    public async Task RiskyLinkLabelsAreRejectedBeforeBrowserConnection()
    {
        var root = NewRoot();
        try
        {
            using var manager = new BraveProfileManager(root);
            var profile = await manager.CreateProfileAsync("Test", "Browser automation validation");
            var tool = new BrowserAutomationTool(manager);

            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "click_link",
                ["profile_id"] = profile.Id,
                ["link_name"] = "Delete account"
            });

            Assert.False(result.Success);
            Assert.Contains("will not activate it", result.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SearchRejectsOverlongQueriesBeforeBrowserConnection()
    {
        var root = NewRoot();
        try
        {
            using var manager = new BraveProfileManager(root);
            var profile = await manager.CreateProfileAsync("Test", "Browser automation validation");
            var tool = new BrowserAutomationTool(manager);

            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "search",
                ["profile_id"] = profile.Id,
                ["query"] = new string('x', 401)
            });

            Assert.False(result.Success);
            Assert.Contains("at most 400 characters", result.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InspectionRequiresAnAlreadyRunningAutomationEnabledProfile()
    {
        var root = NewRoot();
        try
        {
            using var manager = new BraveProfileManager(root);
            var profile = await manager.CreateProfileAsync("Test", "Browser automation validation");
            var tool = new BrowserAutomationTool(manager);

            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "inspect",
                ["profile_id"] = profile.Id
            });

            Assert.False(result.Success);
            Assert.Contains("not running", result.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UnknownActionsAreRejected()
    {
        var root = NewRoot();
        try
        {
            using var manager = new BraveProfileManager(root);
            var tool = new BrowserAutomationTool(manager);

            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "submit_form",
                ["profile_id"] = Guid.NewGuid().ToString("N")
            });

            Assert.False(result.Success);
            Assert.Contains("Supported actions", result.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
