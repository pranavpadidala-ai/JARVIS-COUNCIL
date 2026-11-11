using Council.App;

namespace Council.Dev.Tests;

public sealed class WorkspaceToolTests
{
    [Fact]
    public async Task Create_and_read_file_inside_workspace()
    {
        var root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "notes"));
        var create = new WorkspaceTool("create_file", "create", root);
        var read = new WorkspaceTool("read_file", "read", root);

        var created = await create.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = "notes/hello.txt",
            ["content"] = "Hello JARVIS"
        });
        var loaded = await read.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = "notes/hello.txt"
        });

        Assert.True(created.Success);
        Assert.True(loaded.Success);
        Assert.Equal("Hello JARVIS", loaded.Message);
    }

    [Fact]
    public async Task Create_file_does_not_overwrite_existing_file()
    {
        var root = CreateWorkspace();
        var path = Path.Combine(root, "keep.txt");
        await File.WriteAllTextAsync(path, "original");
        var tool = new WorkspaceTool("create_file", "create", root);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = "keep.txt",
            ["content"] = "replacement"
        });

        Assert.False(result.Success);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData(".git/config")]
    [InlineData("bin/output.txt")]
    [InlineData(".env")]
    [InlineData("appsettings.json")]
    public async Task Protected_or_outside_paths_are_rejected(string path)
    {
        var root = CreateWorkspace();
        var tool = new WorkspaceTool("read_file", "read", root);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = path
        });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Search_skips_protected_folders()
    {
        var root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, ".git", "config"), "secret-marker");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "public class App { }");
        var tool = new WorkspaceTool("search_files", "search", root);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "secret-marker"
        });

        Assert.True(result.Success);
        Assert.Contains("No matches", result.Message);
    }

    [Fact]
    public async Task List_files_returns_workspace_entries()
    {
        var root = CreateWorkspace();
        await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), "class App {}");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        var tool = new WorkspaceTool("list_files", "list", root);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = ""
        });

        Assert.True(result.Success);
        Assert.Contains("Program.cs", result.Message);
        Assert.Contains("[DIR] src", result.Message);
        Assert.DoesNotContain("obj", result.Message);
    }

    [Fact]
    public async Task Workspace_root_cannot_be_a_symbolic_link()
    {
        var target = CreateWorkspace();
        var link = target + "-link";
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Assert.Throws<InvalidOperationException>(() =>
                new WorkspaceTool("list_files", "list", link));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Some Windows configurations do not permit creating symbolic links.
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    private static string CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "JARVIS-ToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
