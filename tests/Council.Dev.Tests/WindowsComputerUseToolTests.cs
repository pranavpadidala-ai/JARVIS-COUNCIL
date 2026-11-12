using Council.App.Windows;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.Dev.Tests;

public sealed class WindowsComputerUseToolTests
{
    [Fact]
    public async Task RequiresAnExplicitlyArmedSessionBeforeObservingOrActing()
    {
        var desktop = new FakeDesktop();
        var tool = new WindowsComputerUseTool(new FakeVision(), desktop);
        var observe = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "observe" });
        var execute = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "execute", ["steps"] = """[{"type":"click","x":10,"y":10}]""" });
        Assert.False(observe.Success);
        Assert.False(execute.Success);
        Assert.Empty(desktop.Actions);
    }

    [Fact]
    public async Task ExecutesBoundedActionsAndVerifiesWithScreenshotAnalysis()
    {
        var desktop = new FakeDesktop();
        var vision = new FakeVision();
        var tool = new WindowsComputerUseTool(vision, desktop);
        var begin = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "begin", ["purpose"] = "Change the visible Abaqus section thickness as requested" });
        Assert.True(begin.Success, begin.Message);
        var observed = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "observe", ["task"] = "Identify the Abaqus property dialog." });
        Assert.True(observed.Success, observed.Message);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "execute", ["task"] = "Verify the section-property dialog reflects the entered value.",
            ["steps"] = """[{"type":"click","x":200,"y":300},{"type":"type","text":"3.0"},{"type":"key","key":"ENTER"}]"""
        });
        Assert.True(result.Success, result.Message);
        Assert.Contains("Executed 3 desktop action(s)", result.Message);
        Assert.Contains("Abaqus property dialog visible", result.Message);
        Assert.Equal(3, desktop.Actions.Count);
        Assert.Equal(2, vision.CallCount);
        Assert.Equal("3.0", desktop.TypedText);
    }

    [Fact]
    public async Task BlocksUnsafeKeysAndOutOfBoundsCoordinates()
    {
        var desktop = new FakeDesktop();
        var tool = new WindowsComputerUseTool(new FakeVision(), desktop);
        var begin = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "begin", ["purpose"] = "Inspect an Abaqus model" });
        Assert.True(begin.Success);
        var observed = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "observe" });
        Assert.True(observed.Success);
        var unsafeKey = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "execute", ["steps"] = """[{"type":"key","key":"ALT+F4"}]""" });
        var outside = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "execute", ["steps"] = """[{"type":"click","x":4000,"y":10}]""" });
        Assert.False(unsafeKey.Success);
        Assert.False(outside.Success);
        Assert.Empty(desktop.Actions);
    }


    [Fact]
    public async Task BlocksSaveAndDeleteKeysUntilTheUserConfirms()
    {
        var desktop = new FakeDesktop();
        var tool = new WindowsComputerUseTool(new FakeVision(), desktop);
        Assert.True((await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "begin", ["purpose"] = "Modify an Abaqus model" })).Success);
        Assert.True((await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "observe" })).Success);
        var blocked = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "execute",
            ["steps"] = """[{"type":"key","key":"CTRL+S"}]"""
        });
        Assert.False(blocked.Success);
        Assert.Empty(desktop.Actions);
        var approved = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "execute",
            ["steps"] = """[{"type":"key","key":"CTRL+S","intent":"save_existing","approval":"USER_CONFIRMED"}]"""
        });
        Assert.True(approved.Success, approved.Message);
        Assert.Contains("key:CTRL+S", desktop.Actions);
    }

    [Fact]
    public async Task RequiresConfirmationForDestructiveGuiIntent()
    {
        var desktop = new FakeDesktop();
        var tool = new WindowsComputerUseTool(new FakeVision(), desktop);
        Assert.True((await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "begin", ["purpose"] = "Submit a verified Abaqus job" })).Success);
        Assert.True((await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "observe" })).Success);
        var blocked = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "execute",
            ["steps"] = """[{"type":"click","x":200,"y":300,"intent":"submit_job"}]"""
        });
        Assert.False(blocked.Success);
        Assert.Empty(desktop.Actions);
    }

    private sealed class FakeVision : IImageUnderstandingProvider
    {
        public int CallCount { get; private set; }
        public Task<string> DescribeImagesAsync(string prompt, IReadOnlyList<AIImage> images, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Assert.Single(images);
            Assert.Equal("image/png", images[0].MimeType);
            return Task.FromResult("Abaqus property dialog visible; the displayed thickness is 3.0 mm.");
        }
    }

    private sealed class FakeDesktop : IWindowsDesktopAdapter
    {
        public bool IsAvailable => true;
        public List<string> Actions { get; } = [];
        public string TypedText { get; private set; } = "";
        public (int Width, int Height) GetPrimaryScreenSize() => (1920, 1080);
        public byte[] CapturePrimaryScreen() => [1, 2, 3];
        public void Click(int x, int y, string button, int clicks) => Actions.Add($"click:{x},{y}:{button}:{clicks}");
        public void Drag(int startX, int startY, int endX, int endY) => Actions.Add($"drag:{startX},{startY}:{endX},{endY}");
        public void TypeText(string text) { TypedText += text; Actions.Add("type:" + text); }
        public void PressKey(string key) => Actions.Add("key:" + key);
        public void Scroll(int x, int y, int delta) => Actions.Add($"scroll:{x},{y}:{delta}");
    }
}
