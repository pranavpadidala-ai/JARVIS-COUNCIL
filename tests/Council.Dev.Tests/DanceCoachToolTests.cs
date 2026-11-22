using Council.App.Health;
using Xunit;

namespace Council.Dev.Tests;

public sealed class DanceCoachToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jarvis-dance-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly DateTimeOffset _now = new(2026, 11, 22, 18, 0, 0, TimeSpan.FromHours(5.5));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task SummarisesTheScoreAndLogsWorkout()
    {
        var video = Path.Combine(_dir, "naatu naatu.mp4");
        File.WriteAllText(video, "");
        IReadOnlyList<string>? cli = null;
        var habits = new HabitTracker(Path.Combine(_dir, "health.json"));
        var tool = new DanceCoachTool((args, _) =>
        {
            cli = args;
            return Task.FromResult("{\"ok\": true, \"scored\": true, \"average\": 71.4, \"seconds\": 90, \"weaker_part\": \"legs\", " +
                                   "\"weakest_moments\": [{\"at_seconds\": 35, \"score\": 48}]}");
        }, habits, () => _now);

        var result = await tool.ExecuteAsync(new() { ["video"] = video, ["seconds"] = "90" });
        Assert.True(result.Success);
        Assert.Contains("Good: average match 71%", result.Message);
        Assert.Contains("legs", result.Message);
        Assert.Contains("0:35 (48%)", result.Message);
        Assert.Contains("--mirror", cli!);
        Assert.Equal(2, habits.TodayValue("workout", _now));
    }

    [Fact]
    public async Task RejectsMissingOrNonVideoFiles()
    {
        var tool = new DanceCoachTool((_, _) => Task.FromResult("{}"), new HabitTracker(Path.Combine(_dir, "h.json")), () => _now);
        Assert.False((await tool.ExecuteAsync(new() { ["video"] = Path.Combine(_dir, "missing.mp4") })).Success);
        var text = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(text, "");
        Assert.False((await tool.ExecuteAsync(new() { ["video"] = text })).Success);
    }
}
