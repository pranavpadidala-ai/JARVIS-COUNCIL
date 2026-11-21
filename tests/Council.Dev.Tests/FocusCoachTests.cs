using Council.App.Focus;
using Xunit;

namespace Council.Dev.Tests;

public sealed class FocusCoachTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-focus-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly DateTimeOffset _t0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.FromHours(5.5));

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void NudgesOnDistractionsAtMostEveryThreeMinutes()
    {
        var coach = new FocusCoach(_path);
        coach.Start("thesis chapter 3", 45, null, _t0);

        Assert.Null(coach.Check("EN 1999-1-1 notes - Word", _t0.AddMinutes(1)));
        var nudge = coach.Check("Funny cats - YouTube - Brave", _t0.AddMinutes(2));
        Assert.Contains("Youtube can wait", nudge);
        Assert.Contains("thesis chapter 3", nudge);
        Assert.Null(coach.Check("Instagram - Brave", _t0.AddMinutes(3)));       // too soon
        Assert.NotNull(coach.Check("Instagram - Brave", _t0.AddMinutes(5.5)));
        Assert.Equal(2, coach.Active()!.Distractions);
    }

    [Fact]
    public void IgnoresJarvisOwnWindowAndUsesExtraDistractions()
    {
        var coach = new FocusCoach(_path);
        coach.Start("study", 30, new[] { "chess.com" }, _t0);
        Assert.Null(coach.Check("JARVIS chat - how do I block YouTube?", _t0.AddMinutes(1)));
        Assert.NotNull(coach.Check("Play chess - chess.com", _t0.AddMinutes(2)));
    }

    [Fact]
    public void FinishesWithASummaryAndRecordsHistory()
    {
        var coach = new FocusCoach(_path);
        coach.Start("design report", 25, null, _t0);
        var done = coach.Check(null, _t0.AddMinutes(26));
        Assert.Contains("Focus session done", done);
        Assert.Null(coach.Active());

        var record = Assert.Single(coach.History());
        Assert.True(record.Completed);
        Assert.Equal(25, record.ActualMinutes);
        Assert.Contains("1 session(s)", FocusCoach.Report(coach.History(), _t0.AddHours(1), 7));
    }

    [Fact]
    public async Task ToolStartsAndStopsSessions()
    {
        var now = _t0;
        var tool = new FocusTool(new FocusCoach(_path), () => now);
        var start = await tool.ExecuteAsync(new() { ["action"] = "start", ["minutes"] = "50", ["goal"] = "aluminium HAZ" });
        Assert.True(start.Success);
        now = _t0.AddMinutes(20);
        var stop = await tool.ExecuteAsync(new() { ["action"] = "stop" });
        Assert.Contains("20 of 50 min", stop.Message);

        var bad = await tool.ExecuteAsync(new() { ["action"] = "start", ["minutes"] = "1000" });
        Assert.False(bad.Success);
    }
}
