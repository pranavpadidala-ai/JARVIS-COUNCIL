using System.Text.Json;

namespace Council.App.Proactive;

/// <summary>What the desktop app knows right now; anything unknown is left null.</summary>
public sealed record ProactiveContext(
    DateTimeOffset Now,
    IReadOnlyList<string> PendingHabits,
    int RemindersToday,
    double? WaterToday,
    int? BatteryPercent,
    bool? Charging,
    int? MemoryLoadPercent,
    bool FocusActive,
    bool UserActive);

public sealed record ProactiveSettings(bool Enabled, Dictionary<string, DateTimeOffset> LastShown);

/// <summary>
/// Small, rule-based suggestions the tray shows without being asked: a morning summary, an evening
/// habit check, water and battery nudges, high memory, and a late-night sleep nudge. Each rule has
/// its own cooldown, and only urgent ones (battery, memory) are shown during a focus session.
/// The user can turn the whole thing off from chat.
/// </summary>
public sealed class ProactiveAdvisor
{
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public ProactiveAdvisor(string? path = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("JARVIS_PROACTIVE_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "proactive.json");
    }

    public bool Enabled { get { lock (FileLock) return Load().Enabled; } }

    public void SetEnabled(bool enabled)
    {
        lock (FileLock) { var s = Load(); Save(s with { Enabled = enabled }); }
    }

    /// <summary>Returns at most one suggestion (title, text), and records it so it is not repeated too soon.</summary>
    public (string Title, string Text)? Next(ProactiveContext c)
    {
        lock (FileLock)
        {
            var settings = Load();
            if (!settings.Enabled) return null;
            foreach (var rule in Rules(c))
            {
                if (rule is null) continue;
                var (key, cooldown, perDay, urgent, title, text) = rule.Value;
                if (c.FocusActive && !urgent) continue;
                if (settings.LastShown.TryGetValue(key, out var last))
                {
                    if (perDay && last.LocalDateTime.Date == c.Now.LocalDateTime.Date) continue;
                    if (!perDay && c.Now - last < cooldown) continue;
                }
                settings.LastShown[key] = c.Now;
                Save(settings);
                return (title, text);
            }
            return null;
        }
    }

    private static IEnumerable<(string Key, TimeSpan Cooldown, bool PerDay, bool Urgent, string Title, string Text)?> Rules(ProactiveContext c)
    {
        var hour = c.Now.LocalDateTime.Hour;

        if (c.BatteryPercent is < 20 && c.Charging == false)
            yield return ("battery", TimeSpan.FromMinutes(30), false, true, "Battery low",
                $"Battery at {c.BatteryPercent}%. Plug in the charger, or I can keep things light.");

        if (c.MemoryLoadPercent is >= 90)
            yield return ("memory", TimeSpan.FromMinutes(30), false, true, "Memory almost full",
                $"RAM is {c.MemoryLoadPercent}% used. Closing unused browser tabs or apps will speed things up.");

        if (!c.UserActive) yield break;

        if (hour is >= 6 and < 11)
        {
            var parts = new List<string>();
            if (c.RemindersToday > 0) parts.Add($"{c.RemindersToday} reminder(s) today");
            if (c.PendingHabits.Count > 0) parts.Add($"habits: {string.Join(", ", c.PendingHabits.Take(4))}");
            yield return ("morning", TimeSpan.Zero, true, false, "Good morning",
                parts.Count == 0 ? "Ready when you are. Want to start a focus session for your most important task?"
                                 : "Today: " + string.Join("; ", parts) + ".");
        }

        if (hour is >= 19 and < 23 && c.PendingHabits.Count > 0)
            yield return ("evening-habits", TimeSpan.Zero, true, false, "Habits",
                "Still to do today: " + string.Join(", ", c.PendingHabits.Take(5)) + ".");

        if (hour is >= 11 and < 22 && c.WaterToday is { } water)
        {
            var expected = Math.Floor((hour - 8) / 14.0 * 8);
            if (water < expected - 1)
                yield return ("water", TimeSpan.FromHours(3), false, false, "Water",
                    $"Only {water:0} glass(es) of water so far today. Time for one?");
        }

        if (hour is >= 0 and < 4)
            yield return ("late-night", TimeSpan.Zero, true, false, "It's late",
                "It's past midnight. Good sleep helps tomorrow's work. Want me to set a reminder and wrap up?");
    }

    private ProactiveSettings Load()
    {
        if (!File.Exists(_path)) return new ProactiveSettings(true, new());
        try
        {
            var s = JsonSerializer.Deserialize<ProactiveSettings>(File.ReadAllText(_path), JsonOptions);
            return s is null ? new ProactiveSettings(true, new()) : s with { LastShown = s.LastShown ?? new() };
        }
        catch (JsonException) { return new ProactiveSettings(true, new()); }
    }

    private void Save(ProactiveSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}
