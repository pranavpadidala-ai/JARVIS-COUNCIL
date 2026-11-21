using Council.App.Voice;

namespace Council.Dev.Tests;

public sealed class LocalModelPathsTests
{
    [Fact]
    public void ConfiguredRootWins()
    {
        var root = LocalModelPaths.ResolveRoot(key => key == "JARVIS_MODELS_ROOT" ? @"E:\JARVIS\Models\LocalAI" : null);

        Assert.Equal(@"E:\JARVIS\Models\LocalAI", root);
    }

    [Fact]
    public void RepositoryModelsFolderIsFoundFromTheAppFolder()
    {
        using var workspace = new ModelsWorkspace();
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "JARVIS.Council.sln"), string.Empty);
        var models = Directory.CreateDirectory(System.IO.Path.Combine(workspace.Path, "Models", "LocalAI")).FullName;
        var appFolder = Directory.CreateDirectory(System.IO.Path.Combine(workspace.Path, "src", "Council.Desktop", "bin", "Release")).FullName;

        var root = LocalModelPaths.ResolveRoot(_ => null, appFolder);

        Assert.Equal(models, root);
    }

    [Fact]
    public void FallsBackToLocalAppDataWithoutRepositoryModels()
    {
        using var workspace = new ModelsWorkspace();

        var root = LocalModelPaths.ResolveRoot(_ => null, workspace.Path);

        Assert.EndsWith("JARVIS", root);
    }

    private sealed class ModelsWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jarvis-models-" + Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
