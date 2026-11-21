using Council.App.Health;
using Xunit;

namespace Council.Dev.Tests;

public sealed class HabitTrackerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-health-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly DateTimeOffset _day1 = new(2026, 11, 20, 8, 0, 0, TimeSpan.FromHours(5.5));

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void CountsStreaksAcrossDays()
    {
        var t = new HabitTracker(_path);
        t.AddHabit("morning walk", _day1);
        Assert.Contains("Streak: 1", t.MarkDone("walk", _day1));
        Assert.Contains("Streak: 2", t.MarkDone("morning walk", _day1.AddDays(1)));
        Assert.Contains("Streak: 2", t.MarkDone("morning walk", _day1.AddDays(1).AddHours(3))); // same day twice
        Assert.Contains("streak 2", t.Today(_day1.AddDays(2)));                                 // not lost before today's check-in
        Assert.Contains("streak 0", t.Today(_day1.AddDays(4)));
    }

    [Fact]
    public void AddsUpWaterAndKeepsLatestWeight()
    {
        var t = new HabitTracker(_path);
        t.Log("water", 3, _day1);
        Assert.Contains("Today: 5 glasses", t.Log("water", 2, _day1.AddHours(2)));
        t.Log("weight", 71.5, _day1);
        Assert.Contains("Today: 71.2 kg", t.Log("weight", 71.2, _day1.AddHours(1)));
        Assert.Throws<ArgumentException>(() => t.Log("sleep", 30, _day1));
        Assert.Throws<ArgumentException>(() => t.Log("coffee", 1, _day1));
    }

    [Fact]
    public async Task ToolReportsAWeek()
    {
        var tool = new HabitTool(new HabitTracker(_path), () => _day1);
        await tool.ExecuteAsync(new() { ["action"] = "add_habit", ["name"] = "read 20 pages" });
        await tool.ExecuteAsync(new() { ["action"] = "done", ["name"] = "read" });
        await tool.ExecuteAsync(new() { ["action"] = "log", ["metric"] = "sleep", ["value"] = "6.5" });
        var report = await tool.ExecuteAsync(new() { ["action"] = "report" });
        Assert.True(report.Success);
        Assert.Contains("read 20 pages: 1/7 days", report.Message);
        Assert.Contains("sleep: average 6.5 hours", report.Message);

        var bad = await tool.ExecuteAsync(new() { ["action"] = "log", ["metric"] = "water", ["value"] = "lots" });
        Assert.False(bad.Success);
    }
}
