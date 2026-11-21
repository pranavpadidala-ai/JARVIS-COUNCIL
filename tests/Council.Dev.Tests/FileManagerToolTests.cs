using Council.App.Files;
using Xunit;

namespace Council.Dev.Tests;

public sealed class FileManagerToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jarvis-files-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _recycled = new();
    private readonly FileManagerTool _tool;

    public FileManagerToolTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "blocked"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        File.WriteAllText(Path.Combine(_root, "docs", "report.pdf"), "x");
        File.WriteAllText(Path.Combine(_root, "docs", "notes.txt"), "hello");
        _tool = new FileManagerTool(new[] { Path.Combine(_root, "blocked") }, path => _recycled.Add(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private Task<Council.Execution.Tools.ToolResult> Run(params (string Key, string Value)[] args) =>
        _tool.ExecuteAsync(args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public async Task ListsAndFindsFiles()
    {
        var list = await Run(("action", "list"), ("path", Path.Combine(_root, "docs")));
        Assert.True(list.Success);
        Assert.Contains("report.pdf", list.Message);

        var found = await Run(("action", "find"), ("path", _root), ("pattern", "*.pdf"));
        Assert.Contains("report.pdf", found.Message);
        Assert.DoesNotContain("notes.txt", found.Message);
    }

    [Fact]
    public async Task CopiesMovesAndRenamesWithoutOverwriting()
    {
        var docs = Path.Combine(_root, "docs");
        var target = Directory.CreateDirectory(Path.Combine(_root, "archive")).FullName;

        Assert.True((await Run(("action", "copy"), ("path", Path.Combine(docs, "notes.txt")), ("to", target))).Success);
        Assert.True(File.Exists(Path.Combine(target, "notes.txt")));

        var again = await Run(("action", "copy"), ("path", Path.Combine(docs, "notes.txt")), ("to", target));
        Assert.False(again.Success);
        Assert.Contains("nothing was overwritten", again.Message);

        Assert.True((await Run(("action", "move"), ("path", Path.Combine(docs, "report.pdf")), ("to", target))).Success);
        Assert.False(File.Exists(Path.Combine(docs, "report.pdf")));

        Assert.True((await Run(("action", "rename"), ("path", Path.Combine(target, "report.pdf")), ("new_name", "thesis.pdf"))).Success);
        Assert.True(File.Exists(Path.Combine(target, "thesis.pdf")));
    }

    [Fact]
    public async Task DeleteGoesToTheRecycleBin()
    {
        var file = Path.Combine(_root, "docs", "notes.txt");
        var result = await Run(("action", "delete"), ("path", file));
        Assert.True(result.Success);
        Assert.Contains("Recycle Bin", result.Message);
        Assert.Equal(new[] { file }, _recycled);
    }

    [Fact]
    public async Task RefusesToChangeBlockedFolders()
    {
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(Path.Combine(blocked, "model.gguf"), "m");

        var delete = await Run(("action", "delete"), ("path", Path.Combine(blocked, "model.gguf")));
        Assert.False(delete.Success);
        Assert.Empty(_recycled);

        var into = await Run(("action", "copy"), ("path", Path.Combine(_root, "docs", "notes.txt")), ("to", blocked));
        Assert.False(into.Success);
    }

    [Fact]
    public async Task RefusesToMoveAFolderIntoItself()
    {
        var docs = Path.Combine(_root, "docs");
        var inner = Directory.CreateDirectory(Path.Combine(docs, "inner")).FullName;
        var result = await Run(("action", "move"), ("path", docs), ("to", inner));
        Assert.False(result.Success);
    }
}
