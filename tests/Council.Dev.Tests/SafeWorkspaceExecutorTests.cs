using Council.Dev.Actions;
using Council.Dev.Workspace;

namespace Council.Dev.Tests;

public class SafeWorkspaceExecutorTests
{
    [Fact]
    public async Task Create_file_creates_file_inside_workspace()
    {
        var root = CreateWorkspace();

        var executor =
            new SafeWorkspaceExecutor(root);

        var action = new DevelopmentAction(
            DevelopmentActionType.CreateFile,
            "src/Test.cs",
            "public class Test {}");

        var result =
            await executor.ExecuteAsync(action);

        Assert.True(result.Success);

        Assert.Equal(
            "public class Test {}",
            await File.ReadAllTextAsync(
                Path.Combine(root, "src", "Test.cs")));
    }

    [Fact]
    public async Task Read_file_returns_content()
    {
        var root = CreateWorkspace();

        var path =
            Path.Combine(root, "Test.cs");

        await File.WriteAllTextAsync(
            path,
            "hello");

        var executor =
            new SafeWorkspaceExecutor(root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.ReadFile,
                    "Test.cs"));

        Assert.True(result.Success);
        Assert.Equal("hello", result.Content);
    }

    [Fact]
    public async Task Path_traversal_is_blocked()
    {
        var root = CreateWorkspace();

        var executor =
            new SafeWorkspaceExecutor(root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.CreateFile,
                    @"..\outside.txt",
                    "danger"));

        Assert.False(result.Success);
        Assert.Contains(
            "outside the workspace",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Git_directory_is_protected()
    {
        var root = CreateWorkspace();

        Directory.CreateDirectory(
            Path.Combine(root, ".git"));

        var executor =
            new SafeWorkspaceExecutor(root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.CreateFile,
                    ".git/config",
                    "danger"));

        Assert.False(result.Success);
        Assert.Contains(
            "protected",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Existing_file_cannot_be_created_again()
    {
        var root = CreateWorkspace();

        var path =
            Path.Combine(root, "Test.cs");

        await File.WriteAllTextAsync(
            path,
            "original");

        var executor =
            new SafeWorkspaceExecutor(root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.CreateFile,
                    "Test.cs",
                    "replacement"));

        Assert.False(result.Success);

        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Build_action_is_not_executed_by_workspace()
    {
        var root = CreateWorkspace();

        var executor =
            new SafeWorkspaceExecutor(root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.RunBuild,
                    "."));

        Assert.False(result.Success);
        Assert.Contains(
            "not handled",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_skips_protected_directories_before_traversal()
    {
        var root = CreateWorkspace();
        var protectedDirectory = Path.Combine(root, ".git");
        Directory.CreateDirectory(protectedDirectory);
        Directory.CreateDirectory(Path.Combine(root, "src"));

        await File.WriteAllTextAsync(
            Path.Combine(protectedDirectory, "Secret.cs"),
            "needle");
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "Visible.cs"),
            "needle");

        var executor = new SafeWorkspaceExecutor(root);
        var result = await executor.ExecuteAsync(
            new DevelopmentAction(
                DevelopmentActionType.SearchCode,
                "needle"));

        Assert.True(result.Success);
        Assert.Contains("Visible.cs", result.Content);
        Assert.DoesNotContain(".git", result.Content);
    }

    [Fact]
    public async Task Search_skips_protected_configuration_files()
    {
        var root = CreateWorkspace();
        await File.WriteAllTextAsync(
            Path.Combine(root, "appsettings.json"),
            "needle");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Program.cs"),
            "needle");

        var executor = new SafeWorkspaceExecutor(root);
        var result = await executor.ExecuteAsync(
            new DevelopmentAction(
                DevelopmentActionType.SearchCode,
                "needle"));

        Assert.True(result.Success);
        Assert.Contains("Program.cs", result.Content);
        Assert.DoesNotContain("appsettings.json", result.Content);
    }

    [Fact]
    public async Task List_files_returns_nested_workspace_files_and_skips_protected_folders()
    {
        var root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, ".git", "objects"));

        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "Main.cs"),
            "class Main {}");
        await File.WriteAllTextAsync(
            Path.Combine(root, ".git", "objects", "internal"),
            "must not be listed");

        var executor = new SafeWorkspaceExecutor(root);
        var result = await executor.ExecuteAsync(
            new DevelopmentAction(
                DevelopmentActionType.ListFiles,
                "."));

        Assert.True(result.Success);
        Assert.Contains(Path.Combine("src", "Main.cs"), result.Content);
        Assert.DoesNotContain(".git", result.Content);
    }

    private static string CreateWorkspace()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "JARVIS",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        return root;
    }
}
