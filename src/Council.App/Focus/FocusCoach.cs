using System.Text.Json;

namespace Council.App.Focus;

public sealed record FocusSession(string Goal, DateTimeOffset StartedAt, DateTimeOffset EndsAt, List<string> ExtraDistractions,
    int Distractions = 0, DateTimeOffset? LastNudgeAt = null, string? LastDistraction = null);

public sealed record FocusRecord(string Goal, DateTimeOffset StartedAt, int PlannedMinutes, int ActualMinutes, int Distractions, bool Completed);

public sealed record FocusState(FocusSession? Active, List<FocusRecord> History);

/// <summary>
/// Focus sessions with gentle distraction nudges. The chat starts and stops sessions; the desktop app
/// calls <see cref="Check"/> every 30 seconds with the title of the window in front, and shows any
/// returned message as a tray notification. Window titles are only looked at while a session runs,
/// and they are never stored: only the matched site name is counted.
/// </summary>
public sealed class FocusCoach
{
    public static readonly string[] DefaultDistractions =
    {
        "youtube", "instagram", "facebook", "netflix", "prime video", "hotstar", "jiocinema", "reddit",
        "twitter", " x.com", "whatsapp", "snapchat", "tiktok", "moj", "sharechat", "twitch", "telegram",
        "cricbuzz", "espncricinfo", "amazon.in", "flipkart", "myntra", "steam", "epic games"
    };

    public static readonly TimeSpan NudgeInterval = TimeSpan.FromMinutes(3);
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public FocusCoach(string? path = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("JARVIS_FOCUS_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "focus.json");
    }

    public FocusSession Start(string goal, int minutes, IEnumerable<string>? extraDistractions, DateTimeOffset now)
    {
        if (minutes is < 5 or > 240) throw new ArgumentException("Focus sessions can be 5 to 240 minutes.");
        goal = string.IsNullOrWhiteSpace(goal) ? "focused work" : goal.Trim();
        if (goal.Length > 200) goal = goal[..200];
        lock (FileLock)
        {
            var state = Load();
            if (state.Active is not null) Finish(state, now, completed: false);
            var extras = (extraDistractions ?? Array.Empty<string>())
                .Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length >= 3).Distinct().Take(30).ToList();
            var session = new FocusSession(goal, now, now.AddMinutes(minutes), extras);
            Save(state with { Active = session });
            return session;
        }
    }

    public FocusRecord? Stop(DateTimeOffset now)
    {
        lock (FileLock)
        {
            var state = Load();
            if (state.Active is null) return null;
            var record = Finish(state, now, completed: now >= state.Active.EndsAt);
            Save(state with { Active = null });
            return record;
        }
    }

    public FocusSession? Active() { lock (FileLock) return Load().Active; }

    public IReadOnlyList<FocusRecord> History() { lock (FileLock) return Load().History; }

    /// <summary>Returns a tray message (nudge or session summary), or null when there is nothing to say.</summary>
    public string? Check(string? foregroundTitle, DateTimeOffset now)
    {
        lock (FileLock)
        {
            var state = Load();
            var session = state.Active;
            if (session is null) return null;

            if (now >= session.EndsAt)
            {
                var record = Finish(state, now, completed: true);
                Save(state with { Active = null });
                return $"Focus session done: {record.Goal}. {record.PlannedMinutes} min, " +
                       (record.Distractions == 0 ? "no distractions. Great work!" : $"{record.Distractions} distraction nudge(s). Take a 5-minute break.");
            }

            var hit = MatchDistraction(foregroundTitle, session.ExtraDistractions);
            if (hit is null) return null;
            if (session.LastNudgeAt is { } last && now - last < NudgeInterval) return null;

            Save(state with { Active = session with { Distractions = session.Distractions + 1, LastNudgeAt = now, LastDistraction = hit } });
            var left = (int)Math.Ceiling((session.EndsAt - now).TotalMinutes);
            return $"{Capitalize(hit.Trim())} can wait. {left} min left on: {session.Goal}.";
        }
    }

    public static string? MatchDistraction(string? title, IEnumerable<string> extras)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var lower = " " + title.ToLowerInvariant();
        // JARVIS's own windows never count, even when the chat is talking about YouTube.
        if (lower.Contains("jarvis")) return null;
        return DefaultDistractions.Concat(extras).FirstOrDefault(lower.Contains);
    }

    public static string Report(IReadOnlyList<FocusRecord> history, DateTimeOffset now, int days)
    {
        var since = now.AddDays(-days);
        var recent = history.Where(r => r.StartedAt >= since).ToList();
        if (recent.Count == 0) return $"No focus sessions in the last {days} day(s).";
        var minutes = recent.Sum(r => r.ActualMinutes);
        var lines = recent.OrderByDescending(r => r.StartedAt).Take(15).Select(r =>
            $"- {r.StartedAt.ToLocalTime():dd MMM HH:mm} {r.Goal}: {r.ActualMinutes}/{r.PlannedMinutes} min, {r.Distractions} nudge(s){(r.Completed ? "" : " (stopped early)")}");
        return $"Last {days} day(s): {recent.Count} session(s), {minutes / 60}h {minutes % 60}m focused, " +
               $"{recent.Sum(r => r.Distractions)} distraction nudge(s).\n" + string.Join("\n", lines);
    }

    private static FocusRecord Finish(FocusState state, DateTimeOffset now, bool completed)
    {
        var s = state.Active!;
        var end = now < s.EndsAt ? now : s.EndsAt;
        var record = new FocusRecord(s.Goal, s.StartedAt, (int)Math.Round((s.EndsAt - s.StartedAt).TotalMinutes),
            Math.Max(0, (int)Math.Round((end - s.StartedAt).TotalMinutes)), s.Distractions, completed);
        state.History.Add(record);
        if (state.History.Count > 1000) state.History.RemoveRange(0, state.History.Count - 1000);
        return record;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private FocusState Load()
    {
        if (!File.Exists(_path)) return new FocusState(null, new List<FocusRecord>());
        var state = JsonSerializer.Deserialize<FocusState>(File.ReadAllText(_path), JsonOptions);
        return state is null ? new FocusState(null, new List<FocusRecord>()) : state with { History = state.History ?? new List<FocusRecord>() };
    }

    private void Save(FocusState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}
