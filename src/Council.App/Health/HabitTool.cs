using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Health;

/// <summary>Chat access to the local habit and health log.</summary>
public sealed class HabitTool : ITool
{
    private readonly HabitTracker _tracker;
    private readonly Func<DateTimeOffset> _clock;

    public HabitTool(HabitTracker? tracker = null, Func<DateTimeOffset>? clock = null)
    {
        _tracker = tracker ?? new HabitTracker();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name => "habits";

    public string Description =>
        "Track the user's habits and health on this laptop. Arguments: action = add_habit | remove_habit | done | log | today | report. " +
        "add_habit/remove_habit/done: name (e.g. 'morning walk', 'read 20 pages'). " +
        "log: metric (water in glasses, sleep in hours, steps, workout in minutes, weight in kg, mood 1-5) and value. " +
        "report: optional days (default 7). Give encouraging, practical tips; this is not medical advice.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        var now = _clock();
        try
        {
            var message = Arg("action").ToLowerInvariant() switch
            {
                "add_habit" => _tracker.AddHabit(Arg("name"), now),
                "remove_habit" => _tracker.RemoveHabit(Arg("name")),
                "done" => _tracker.MarkDone(Arg("name"), now),
                "log" => HabitTracker.TryParseValue(Arg("value"), out var value)
                    ? _tracker.Log(Arg("metric"), value, now)
                    : throw new ArgumentException("value must be a number."),
                "today" => _tracker.Today(now),
                "report" => _tracker.Report(now, int.TryParse(Arg("days"), out var d) ? Math.Clamp(d, 1, 365) : 7),
                _ => throw new ArgumentException("action must be add_habit, remove_habit, done, log, today or report.")
            };
            return Task.FromResult(new ToolResult(true, message));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(new ToolResult(false, ex.Message));
        }
    }
}
