using Council.App.Reminders;
using Xunit;

namespace Council.Dev.Tests;

public sealed class ReminderTests : IDisposable
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.CreateCustomTimeZone("IST-test", TimeSpan.FromHours(5.5), "IST", "IST");
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 18, 0, 0, TimeSpan.FromHours(5.5));
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-reminders-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Theory]
    [InlineData("in 20 minutes", 0, 20)]
    [InlineData("in 2 hours", 2, 0)]
    [InlineData("in 1 day", 24, 0)]
    public void ParsesRelativeTimes(string when, int hours, int minutes) =>
        Assert.Equal(Now + new TimeSpan(hours, minutes, 0), ReminderTimeParser.Parse(when, Now, India));

    [Theory]
    [InlineData("tomorrow 9:00", 2026, 11, 22, 9, 0)]
    [InlineData("today 6:30 pm", 2026, 11, 21, 18, 30)]
    [InlineData("tomorrow at 7am", 2026, 11, 22, 7, 0)]
    [InlineData("18:30", 2026, 11, 21, 18, 30)]
    [InlineData("17:00", 2026, 11, 22, 17, 0)] // already past today, so tomorrow
    [InlineData("2026-12-01 08:15", 2026, 12, 1, 8, 15)]
    public void ParsesClockTimesInLocalZone(string when, int y, int mo, int d, int h, int mi) =>
        Assert.Equal(new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(5.5)), ReminderTimeParser.Parse(when, Now, India));

    [Theory]
    [InlineData("")]
    [InlineData("whenever")]
    [InlineData("in 400 days")]
    public void RejectsUnclearTimes(string when) =>
        Assert.Throws<ArgumentException>(() => ReminderTimeParser.Parse(when, Now, India));

    [Fact]
    public void DueRemindersAreDeliveredExactlyOnce()
    {
        var store = new ReminderStore(_path);
        store.Add("Check the washing machine", Now.AddMinutes(10), Now);
        store.Add("Submit thesis chapter", Now.AddDays(1), Now);

        Assert.Empty(store.ClaimDue(Now.AddMinutes(5)));
        var due = store.ClaimDue(Now.AddMinutes(11));
        Assert.Single(due);
        Assert.Equal("Check the washing machine", due[0].Text);
        Assert.Empty(store.ClaimDue(Now.AddMinutes(12)));
        Assert.Single(store.ListPending());
    }

    [Fact]
    public void RejectsPastTimesAndCancelsById()
    {
        var store = new ReminderStore(_path);
        Assert.Throws<ArgumentException>(() => store.Add("too late", Now.AddMinutes(-1), Now));
        var reminder = store.Add("Call guide", Now.AddHours(1), Now);
        Assert.True(store.Cancel(reminder.Id));
        Assert.False(store.Cancel(reminder.Id));
        Assert.Empty(store.ListPending());
    }

    [Fact]
    public async Task ToolAddsListsAndCancels()
    {
        var tool = new ReminderTool(new ReminderStore(_path), () => Now);
        var added = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "add", ["text"] = "Drink water", ["when"] = "in 30 minutes" });
        Assert.True(added.Success, added.Message);
        Assert.Contains("Reminder set", added.Message);

        var listed = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "list" });
        Assert.Contains("Drink water", listed.Message);

        var bad = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "add", ["text"] = "x", ["when"] = "someday" });
        Assert.False(bad.Success);
    }

    [Fact]
    public void DamagedFileIsReportedNotOverwritten()
    {
        File.WriteAllText(_path, "{ not json");
        var store = new ReminderStore(_path);
        Assert.Throws<InvalidOperationException>(() => store.ListPending());
        Assert.Equal("{ not json", File.ReadAllText(_path));
    }
}
