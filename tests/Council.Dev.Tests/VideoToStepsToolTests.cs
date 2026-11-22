using Council.App.Media;
using Xunit;
using static Council.App.Media.VideoToStepsTool;

namespace Council.Dev.Tests;

public sealed class VideoToStepsToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jarvis-v2s-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task WritesTimestampedNotesAndSkipsRepeatedScreens()
    {
        var video = Path.Combine(_dir, "abaqus mesh tutorial.mp4");
        File.WriteAllText(video, "");
        var tool = new VideoToStepsTool((_, max, _) =>
        {
            Assert.Equal(15, max);
            return Task.FromResult(new VideoEvidence(125, new[]
            {
                new Line(4, "speech", "First open the Mesh module."),
                new Line(6, "screen", "Module: Mesh  Seed Part Instance"),
                new Line(10, "screen", "Module: Mesh  Seed Part Instance"),
                new Line(70, "screen", "Approximate global size 5"),
            }));
        }, Path.Combine(_dir, "notes"));

        var result = await tool.ExecuteAsync(new() { ["video"] = video });
        Assert.True(result.Success);
        Assert.Contains("[0:04] [speech] First open the Mesh module.", result.Message);
        Assert.Contains("[1:10] [screen] Approximate global size 5", result.Message);
        Assert.Single(result.Message.Split('\n'), l => l.Contains("Seed Part Instance"));
        Assert.True(File.Exists(Path.Combine(_dir, "notes", "abaqus mesh tutorial - notes.md")));
    }

    [Fact]
    public async Task ReportsEmptyVideosAndBadInput()
    {
        var video = Path.Combine(_dir, "silent.mp4");
        File.WriteAllText(video, "");
        var tool = new VideoToStepsTool((_, _, _) => Task.FromResult(new VideoEvidence(10, Array.Empty<Line>())), Path.Combine(_dir, "notes"));
        Assert.False((await tool.ExecuteAsync(new() { ["video"] = video })).Success);
        Assert.False((await tool.ExecuteAsync(new() { ["video"] = Path.Combine(_dir, "nope.mp4") })).Success);
    }
}
