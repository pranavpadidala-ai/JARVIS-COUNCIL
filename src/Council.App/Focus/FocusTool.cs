using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Focus;

/// <summary>Lets the chat start, stop and report focus sessions. The desktop app sends the nudges.</summary>
public sealed class FocusTool : ITool
{
    private readonly FocusCoach _coach;
    private readonly Func<DateTimeOffset> _clock;

    public FocusTool(FocusCoach? coach = null, Func<DateTimeOffset>? clock = null)
    {
        _coach = coach ?? new FocusCoach();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name => "focus";

    public string Description =>
        "Help the user focus and avoid distractions. Arguments: action = start | stop | status | report. " +
        "start: minutes (5-240, default 25) and goal (what they are working on); optional distractions " +
        "(comma-separated extra site or app names to nudge about). While a session runs, the JARVIS desktop app " +
        "nudges the user if YouTube, Instagram, Netflix, games and similar are in front (at most once every 3 minutes). " +
        "report: optional days (default 7). Suggest a 25-50 minute session with a short break when the user says they are distracted.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        var now = _clock();
        try
        {
            switch (Arg("action").ToLowerInvariant())
            {
                case "start":
                    var minutes = int.TryParse(Arg("minutes"), out var m) ? m : 25;
                    var session = _coach.Start(Arg("goal"), minutes, Arg("distractions").Split(',', StringSplitOptions.RemoveEmptyEntries), now);
                    return Done(true, $"Focus session started: {session.Goal}, {minutes} min, until {session.EndsAt.ToLocalTime():HH:mm}. " +
                                      "JARVIS will nudge if a distracting site or app comes to the front.");
                case "stop":
                    var record = _coach.Stop(now);
                    return Done(true, record is null ? "No focus session was running."
                        : $"Stopped: {record.Goal}, {record.ActualMinutes} of {record.PlannedMinutes} min, {record.Distractions} nudge(s).");
                case "status":
                    var active = _coach.Active();
                    return Done(true, active is null ? "No focus session running."
                        : $"Focusing on {active.Goal}: {Math.Max(0, (int)Math.Ceiling((active.EndsAt - now).TotalMinutes))} min left, {active.Distractions} nudge(s) so far.");
                case "report":
                    var days = int.TryParse(Arg("days"), out var d) ? Math.Clamp(d, 1, 365) : 7;
                    return Done(true, FocusCoach.Report(_coach.History(), now, days));
                default:
                    return Done(false, "action must be start, stop, status or report.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            return Done(false, ex.Message);
        }
    }

    private static Task<ToolResult> Done(bool ok, string message) => Task.FromResult(new ToolResult(ok, message));
}
