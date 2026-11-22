using Council.Execution.Tools;

namespace Council.App.Notify;

/// <summary>Lets the user ask "what notifications did I miss?" and summarise them.</summary>
public sealed class NotificationTool : ITool
{
    private readonly NotificationReader _reader;
    private readonly Func<DateTimeOffset> _clock;

    public NotificationTool(NotificationReader? reader = null, Func<DateTimeOffset>? clock = null)
    {
        _reader = reader ?? new NotificationReader();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name => "notifications";

    public string Description =>
        "Read the user's recent Windows notifications (WhatsApp, mail, Teams, calendar, system alerts) when they ask what they missed. " +
        "Arguments: optional hours (default 12, max 168), app (filter by app name), max (default 30). " +
        "Summarise by importance; do not repeat private message text unless the user asks. Only use when the user asks.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        var hours = int.TryParse(Arg("hours"), out var h) ? Math.Clamp(h, 1, 168) : 12;
        var max = int.TryParse(Arg("max"), out var m) ? Math.Clamp(m, 1, 100) : 30;
        try
        {
            var items = _reader.Recent(_clock().AddHours(-hours), 200)
                .Where(n => Arg("app").Length == 0 || n.App.Contains(Arg("app"), StringComparison.OrdinalIgnoreCase))
                .Take(max).ToList();
            if (items.Count == 0)
                return Task.FromResult(new ToolResult(true, $"No notifications in the last {hours} hour(s)" + (Arg("app").Length > 0 ? $" from {Arg("app")}." : ".")));
            var lines = items.Select(n => $"- {n.At.LocalDateTime:dd MMM HH:mm} [{n.App}] {n.Title}{(n.Body.Length > 0 ? ": " + n.Body : "")}");
            return Task.FromResult(new ToolResult(true, $"{items.Count} notification(s), newest first:\n" + string.Join("\n", lines)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return Task.FromResult(new ToolResult(false, "Could not read notifications: " + ex.Message));
        }
    }
}
