using Council.Execution.Tools;

namespace Council.App.Reminders;

/// <summary>Lets the assistant create, list and cancel local reminders. The desktop app delivers them.</summary>
public sealed class ReminderTool : ITool
{
    private readonly ReminderStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public ReminderTool(ReminderStore? store = null, Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? new ReminderStore();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name => "reminders";

    public string Description =>
        "Create, list or cancel the user's reminders on this laptop. Arguments: action (add|list|cancel); " +
        "for add: text and when ('in 20 minutes', 'in 2 hours', 'tomorrow 9:00', 'today 6:30 pm', '18:30' or '2026-11-22 18:30', local time); " +
        "for cancel: id. The JARVIS desktop app shows a notification when a reminder is due, so it must be running. " +
        "Only say a reminder was set after this tool confirms it.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        var action = args.TryGetValue("action", out var a) ? a.Trim().ToLowerInvariant() : "";
        try
        {
            return Task.FromResult(action switch
            {
                "add" => Add(args),
                "list" => List(),
                "cancel" => Cancel(args),
                _ => new ToolResult(false, "action must be add, list or cancel.")
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new ToolResult(false, ex.Message));
        }
    }

    private ToolResult Add(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
            return new ToolResult(false, "add requires text (what to remind about).");
        if (!args.TryGetValue("when", out var when))
            return new ToolResult(false, "add requires when, for example 'in 20 minutes' or 'tomorrow 9:00'.");
        var now = _clock();
        var due = ReminderTimeParser.Parse(when, now);
        var reminder = _store.Add(text, due, now);
        return new ToolResult(true, $"Reminder set (ID {reminder.Id}): \"{reminder.Text}\" at {Describe(reminder.DueAt)}.");
    }

    private ToolResult List()
    {
        var pending = _store.ListPending();
        return pending.Count == 0
            ? new ToolResult(true, "No pending reminders.")
            : new ToolResult(true, "Pending reminders:\n" + string.Join("\n", pending.Select(r => $"[{r.Id}] {Describe(r.DueAt)} - {r.Text}")));
    }

    private ToolResult Cancel(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
            return new ToolResult(false, "cancel requires id. Use action=list to see IDs.");
        return _store.Cancel(id)
            ? new ToolResult(true, $"Reminder {id.Trim()} cancelled.")
            : new ToolResult(false, $"No pending reminder has ID {id.Trim()}.");
    }

    private static string Describe(DateTimeOffset due) =>
        due.ToLocalTime().ToString("ddd dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
