using Council.App.Proactive;
using Xunit;

namespace Council.Dev.Tests;

public sealed class ProactiveAdvisorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-proactive-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static ProactiveContext At(int hour, int minute = 0, IReadOnlyList<string>? habits = null, double? water = null,
        int? battery = null, bool? charging = null, int? memory = null, bool focus = false, bool active = true) =>
        new(new DateTimeOffset(new DateTime(2026, 11, 22, hour, minute, 0, DateTimeKind.Local)), habits ?? Array.Empty<string>(), 0, water, battery, charging, memory, focus, active);

    [Fact]
    public void MorningSummaryShowsOncePerDay()
    {
        var advisor = new ProactiveAdvisor(_path);
        var first = advisor.Next(At(8, habits: new[] { "walk" }));
        Assert.Equal("Good morning", first?.Title);
        Assert.Contains("walk", first?.Text);
        Assert.Null(advisor.Next(At(9, habits: new[] { "walk" })));
    }

    [Fact]
    public void BatteryNudgeHasACooldownAndShowsDuringFocus()
    {
        var advisor = new ProactiveAdvisor(_path);
        Assert.Equal("Battery low", advisor.Next(At(14, battery: 15, charging: false, focus: true))?.Title);
        Assert.Null(advisor.Next(At(14, 10, battery: 14, charging: false, focus: true)));
        Assert.Equal("Battery low", advisor.Next(At(14, 45, battery: 10, charging: false, focus: true))?.Title);
        Assert.Null(advisor.Next(At(15, battery: 10, charging: true)));
    }

    [Fact]
    public void QuietDuringFocusAndWhenTurnedOff()
    {
        var advisor = new ProactiveAdvisor(_path);
        Assert.Null(advisor.Next(At(20, habits: new[] { "read" }, focus: true)));
        advisor.SetEnabled(false);
        Assert.Null(advisor.Next(At(20, habits: new[] { "read" })));
        advisor.SetEnabled(true);
        Assert.Equal("Habits", advisor.Next(At(20, habits: new[] { "read" }))?.Title);
    }

    [Fact]
    public void NudgesAboutWaterWhenBehind()
    {
        var advisor = new ProactiveAdvisor(_path);
        Assert.Equal("Water", advisor.Next(At(16, water: 1))?.Title);
        var fresh = new ProactiveAdvisor(_path + ".2");
        Assert.Null(fresh.Next(At(16, water: 4)));
        if (File.Exists(_path + ".2")) File.Delete(_path + ".2");
    }
}
