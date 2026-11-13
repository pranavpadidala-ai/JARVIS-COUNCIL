using Council.App.Abaqus;

namespace Council.Dev.Tests;

public sealed class AbaqusInstallationDetectorTests
{
    [Fact]
    public void Detect_FindsConfiguredLauncher()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var launcher = Path.Combine(root, "abq2024.bat");
            File.WriteAllText(launcher, "@echo off");

            var detector = new AbaqusInstallationDetector(
                configuredCommand: launcher,
                searchDirectories: [],
                pathLookup: _ => null);

            var result = detector.Detect();

            Assert.True(result.IsAvailable);
            Assert.Equal(Path.GetFullPath(launcher), result.CommandPath);
            Assert.Contains("ABAQUS_COMMAND", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_FindsLauncherInSearchDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var launcher = Path.Combine(root, "abq2024.bat");
            File.WriteAllText(launcher, "@echo off");

            var detector = new AbaqusInstallationDetector(
                configuredCommand: null,
                searchDirectories: [root],
                pathLookup: _ => null);

            var result = detector.Detect();

            Assert.True(result.IsAvailable);
            Assert.Equal(Path.GetFullPath(launcher), result.CommandPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_FindsLauncherThroughPathLookup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var launcher = Path.Combine(root, "abaqus.exe");
            File.WriteAllText(launcher, "test");

            var detector = new AbaqusInstallationDetector(
                configuredCommand: null,
                searchDirectories: [],
                pathLookup: command => command == "abq2024"
                    ? launcher
                    : null);

            var result = detector.Detect();

            Assert.True(result.IsAvailable);
            Assert.Equal(Path.GetFullPath(launcher), result.CommandPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_ReturnsHelpfulMessageWhenNotFound()
    {
        var detector = new AbaqusInstallationDetector(
            configuredCommand: null,
            searchDirectories: [],
            pathLookup: _ => null);

        var result = detector.Detect();

        Assert.False(result.IsAvailable);
        Assert.Null(result.CommandPath);
        Assert.Contains("ABAQUS_COMMAND", result.Message);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "JarvisAbaqusTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }
}
