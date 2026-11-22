using Council.App.Notify;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Council.Dev.Tests;

public sealed class NotificationReaderTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "jarvis-wpntest-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly DateTimeOffset _now = new(2026, 11, 22, 14, 0, 0, TimeSpan.Zero);

    public NotificationReaderTests()
    {
        using var c = new SqliteConnection($"Data Source={_db};Pooling=False");
        c.Open();
        var cmd = c.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE NotificationHandler (RecordId INTEGER, PrimaryId TEXT);" +
            "CREATE TABLE Notification (HandlerId INTEGER, Type TEXT, Payload BLOB, ArrivalTime INTEGER);" +
            "INSERT INTO NotificationHandler VALUES (1, '5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App'), (2, 'Windows.Defender.SecurityCenter');";
        cmd.ExecuteNonQuery();
        Add(c, 1, "<toast><visual><binding template='ToastGeneric'><text>Guide</text><text>Meeting moved to 4 pm</text></binding></visual></toast>", _now.AddHours(-1));
        Add(c, 2, "<toast><visual><binding><text>Scan complete</text></binding></visual></toast>", _now.AddHours(-30));
        Add(c, 1, "<tile><visual><binding><text>ignored tile</text></binding></visual></tile>", _now.AddHours(-1), "tile");
    }

    private static void Add(SqliteConnection c, int handler, string xml, DateTimeOffset at, string type = "toast")
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO Notification VALUES ($h, $t, $p, $a)";
        cmd.Parameters.AddWithValue("$h", handler);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$p", System.Text.Encoding.UTF8.GetBytes(xml));
        cmd.Parameters.AddWithValue("$a", at.UtcDateTime.ToFileTimeUtc());
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) File.Delete(_db);
    }

    [Fact]
    public void ReadsRecentToastsFromACopy()
    {
        var items = new NotificationReader(_db).Recent(_now.AddHours(-12), 50);
        var only = Assert.Single(items);
        Assert.Equal("WhatsApp", only.App);
        Assert.Equal("Guide", only.Title);
        Assert.Equal("Meeting moved to 4 pm", only.Body);
    }

    [Fact]
    public async Task ToolFiltersByAppAndTime()
    {
        var tool = new NotificationTool(new NotificationReader(_db), () => _now);
        var day = await tool.ExecuteAsync(new() { ["hours"] = "48" });
        Assert.Contains("2 notification(s)", day.Message);
        var filtered = await tool.ExecuteAsync(new() { ["hours"] = "48", ["app"] = "whatsapp" });
        Assert.Contains("Meeting moved", filtered.Message);
        Assert.DoesNotContain("Scan complete", filtered.Message);
    }

    [Fact]
    public void ParsesAppNames() => Assert.Equal("Security Center", NotificationReader.FriendlyApp("Windows.Defender.SecurityCenter"));
}
