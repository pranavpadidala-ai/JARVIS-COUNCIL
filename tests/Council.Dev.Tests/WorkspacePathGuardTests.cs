using Council.Bridge;

namespace Council.Dev.Tests;

public sealed class WorkspacePathGuardTests
{
    [Fact]
    public void Resolve_AllowsPathsInsideWorkspace()
    {
        var root = CreateRoot();
        try
        {
            var file = Path.Combine(root, "src", "Example.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "class Example {}");

            var guard = new WorkspacePathGuard(root);

            Assert.Equal(Path.GetFullPath(file), guard.Resolve("src/Example.cs"));
            Assert.Equal("class Example {}", guard.ReadTextFile("src/Example.cs"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("src/../../outside.txt")]
    [InlineData("C:/Windows/win.ini")]
    public void Resolve_RejectsTraversalAndDriveQualifiedPaths(string path)
    {
        var root = CreateRoot();
        try
        {
            var guard = new WorkspacePathGuard(root);
            Assert.Throws<UnauthorizedAccessException>(() => guard.Resolve(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadTextFile_RejectsUnapprovedFileTypes()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.json"), "{ \"apiKey\": \"not-for-tools\" }");
            var guard = new WorkspacePathGuard(root);

            Assert.Throws<UnauthorizedAccessException>(() => guard.ReadTextFile("settings.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListEntries_HidesGitAndBuildDirectories()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "README.md"), "JARVIS");

            var entries = new WorkspacePathGuard(root).ListEntries();
            var names = entries.Select(entry => (string)entry.GetType().GetProperty("name")!.GetValue(entry)!).ToArray();

            Assert.Contains("src", names);
            Assert.Contains("README.md", names);
            Assert.DoesNotContain(".git", names);
            Assert.DoesNotContain("bin", names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "jarvis-bridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
