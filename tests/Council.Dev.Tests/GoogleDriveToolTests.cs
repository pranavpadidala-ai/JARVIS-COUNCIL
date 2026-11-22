using Council.App.Files;
using Xunit;

namespace Council.Dev.Tests;

public sealed class GoogleDriveToolTests : IDisposable
{
    private readonly string _downloads = Path.Combine(Path.GetTempPath(), "jarvis-drive-" + Guid.NewGuid().ToString("N"));
    private readonly List<string[]> _calls = new();

    public void Dispose()
    {
        if (Directory.Exists(_downloads)) Directory.Delete(_downloads, recursive: true);
    }

    private GoogleDriveTool Tool(int exit, string output) =>
        new((args, _) => { _calls.Add(args.ToArray()); return Task.FromResult((exit, output)); }, _downloads);

    [Fact]
    public async Task ListsFoldersBeforeFiles()
    {
        const string json = "[{\"Path\":\"thesis.pdf\",\"Name\":\"thesis.pdf\",\"Size\":2097152,\"ModTime\":\"2026-11-01T10:00:00Z\",\"IsDir\":false}," +
                            "{\"Path\":\"MTech\",\"Name\":\"MTech\",\"Size\":-1,\"ModTime\":\"2026-10-01T10:00:00Z\",\"IsDir\":true}]";
        var result = await Tool(0, json).ExecuteAsync(new() { ["action"] = "list" });
        Assert.True(result.Success);
        Assert.StartsWith("  [folder] MTech", result.Message);
        Assert.Contains("thesis.pdf (2.0 MB", result.Message);
        Assert.Equal(new[] { "lsjson", "gdrive:", "--max-depth", "1", "--no-mimetype" }, _calls[0]);
    }

    [Fact]
    public async Task UploadsWithoutOverwritingAndBlocksPathEscapes()
    {
        var local = Path.Combine(Path.GetTempPath(), "jarvis-up-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(local, "x");
        try
        {
            var result = await Tool(0, "").ExecuteAsync(new() { ["action"] = "upload", ["path"] = local, ["folder"] = "../MTech/./papers" });
            Assert.True(result.Success);
            Assert.Equal("gdrive:MTech/papers/" + Path.GetFileName(local), _calls[0][2]);
            Assert.Contains("--ignore-existing", _calls[0]);
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task ExplainsHowToConnectWhenNotSetUp()
    {
        var result = await Tool(1, "Failed to create file system for \"gdrive:\": didn't find section in config file").ExecuteAsync(new() { ["action"] = "space" });
        Assert.False(result.Success);
        Assert.Contains("Connect-GoogleDrive.ps1", result.Message);
    }

    [Fact]
    public async Task DownloadsNeverReplaceLocalFiles()
    {
        Directory.CreateDirectory(_downloads);
        await File.WriteAllTextAsync(Path.Combine(_downloads, "notes.docx"), "old");
        var result = await Tool(0, "").ExecuteAsync(new() { ["action"] = "download", ["path"] = "MTech/notes.docx" });
        Assert.False(result.Success);
        Assert.Empty(_calls);
    }
}
