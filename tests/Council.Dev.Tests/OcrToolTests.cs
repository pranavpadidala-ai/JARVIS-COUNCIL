using Council.App.Knowledge;
using Xunit;

namespace Council.Dev.Tests;

public sealed class OcrToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jarvis-ocr-" + Guid.NewGuid().ToString("N"));

    public OcrToolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task RejectsMissingFilesUnsupportedTypesAndBadPageLimits()
    {
        var tool = new OcrTool(_root, Path.Combine(_root, "library"), Path.Combine(_root, "script.ps1"));
        File.WriteAllText(Path.Combine(_root, "script.ps1"), "# placeholder");
        File.WriteAllText(Path.Combine(_root, "notes.docx"), "x");
        File.WriteAllText(Path.Combine(_root, "scan.pdf"), "x");

        Assert.False((await tool.ExecuteAsync(new())).Success);
        Assert.Contains("not found", (await tool.ExecuteAsync(new() { ["path"] = "missing.pdf" })).Message);
        Assert.Contains("Supported files", (await tool.ExecuteAsync(new() { ["path"] = "notes.docx" })).Message);
        Assert.Contains("pages must be", (await tool.ExecuteAsync(new() { ["path"] = "scan.pdf", ["pages"] = "5000" })).Message);
        Assert.Contains("simple topic", (await tool.ExecuteAsync(new() { ["path"] = "scan.pdf", ["save_to_library"] = "../.." })).Message);
    }

    [Fact]
    public async Task ReportsAMissingOcrScript()
    {
        File.WriteAllText(Path.Combine(_root, "photo.png"), "x");
        var tool = new OcrTool(_root, Path.Combine(_root, "library"), Path.Combine(_root, "absent.ps1"));
        var result = await tool.ExecuteAsync(new() { ["path"] = "photo.png" });
        Assert.False(result.Success);
        Assert.Contains("OCR script is missing", result.Message);
    }
}
