using System.Globalization;
using System.Text.Json;

namespace Council.App.Health;

public sealed record Habit(string Name, DateOnly CreatedOn);

public sealed record HealthEntry(DateTimeOffset At, string Kind, string Name, double Value);

public sealed record HealthState(List<Habit> Habits, List<HealthEntry> Entries);

/// <summary>
/// Local habit and health log: daily habits with streaks, plus simple metrics (water, sleep, steps,
/// workout minutes, weight, mood). Stored only on this laptop. Not medical advice.
/// </summary>
public sealed class HabitTracker
{
    public static readonly IReadOnlyDictionary<string, (string Unit, double DailyGoal, double Max)> Metrics =
        new Dictionary<string, (string, double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["water"] = ("glasses", 8, 40),
            ["sleep"] = ("hours", 7, 24),
            ["steps"] = ("steps", 8000, 100000),
            ["workout"] = ("minutes", 30, 600),
            ["weight"] = ("kg", 0, 400),
            ["mood"] = ("/5", 0, 5)
        };

    private static readonly HashSet<string> Additive = new(StringComparer.OrdinalIgnoreCase) { "water", "steps", "workout" };
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public HabitTracker(string? path = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("JARVIS_HEALTH_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "health.json");
    }

    public string AddHabit(string name, DateTimeOffset now)
    {
        name = Clean(name);
        lock (FileLock)
        {
            var state = Load();
            if (state.Habits.Any(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return $"'{name}' is already a habit.";
            if (state.Habits.Count >= 50) throw new InvalidOperationException("50 habits is the limit.");
            state.Habits.Add(new Habit(name, DateOnly.FromDateTime(now.LocalDateTime)));
            Save(state);
            return $"Added habit '{name}'. Tell me when you do it and I'll keep the streak.";
        }
    }

    public string RemoveHabit(string name)
    {
        lock (FileLock)
        {
            var state = Load();
            var removed = state.Habits.RemoveAll(h => h.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            Save(state);
            return removed == 0 ? $"No habit called '{name}'." : $"Removed habit '{name.Trim()}' (its history is kept).";
        }
    }

    public string MarkDone(string name, DateTimeOffset now)
    {
        lock (FileLock)
        {
            var state = Load();
            var habit = state.Habits.FirstOrDefault(h => h.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                        ?? state.Habits.FirstOrDefault(h => h.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (habit is null) return $"No habit called '{name}'. Add it first with add_habit.";
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            if (!DoneOn(state, habit.Name, today))
            {
                state.Entries.Add(new HealthEntry(now, "habit", habit.Name, 1));
                Trim(state);
                Save(state);
            }
            return $"Done: {habit.Name}. Streak: {Streak(state, habit.Name, today)} day(s).";
        }
    }

    public string Log(string metric, double value, DateTimeOffset now)
    {
        if (!Metrics.TryGetValue(metric.Trim(), out var info))
            throw new ArgumentException("metric must be water, sleep, steps, workout, weight or mood.");
        if (value < 0 || value > info.Max) throw new ArgumentException($"{metric} must be between 0 and {info.Max}.");
        lock (FileLock)
        {
            var state = Load();
            var name = metric.Trim().ToLowerInvariant();
            state.Entries.Add(new HealthEntry(now, "metric", name, value));
            Trim(state);
            Save(state);
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            var total = DayValue(state, name, today);
            var goal = info.DailyGoal > 0 ? $" (goal {info.DailyGoal:0.#})" : "";
            return $"Logged {value:0.##} {info.Unit} of {name}. Today: {total:0.##} {info.Unit}{goal}.";
        }
    }

    public string Today(DateTimeOffset now)
    {
        lock (FileLock)
        {
            var state = Load();
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            var lines = new List<string> { $"Today ({today:dd MMM}):" };
            foreach (var habit in state.Habits)
                lines.Add($"- [{(DoneOn(state, habit.Name, today) ? "x" : " ")}] {habit.Name} (streak {Streak(state, habit.Name, today)})");
            foreach (var (name, info) in Metrics)
            {
                var value = DayValue(state, name, today);
                if (value is null) continue;
                lines.Add($"- {name}: {value:0.##} {info.Unit}" + (info.DailyGoal > 0 ? $" / {info.DailyGoal:0.#}" : ""));
            }
            return lines.Count == 1 ? "Nothing tracked yet today. Add a habit or log water, sleep, steps or a workout." : string.Join("\n", lines);
        }
    }

    public string Report(DateTimeOffset now, int days)
    {
        lock (FileLock)
        {
            var state = Load();
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            var from = today.AddDays(-(days - 1));
            var lines = new List<string> { $"Last {days} day(s):" };
            foreach (var habit in state.Habits)
            {
                var done = Enumerable.Range(0, days).Count(i => DoneOn(state, habit.Name, from.AddDays(i)));
                lines.Add($"- {habit.Name}: {done}/{days} days, current streak {Streak(state, habit.Name, today)}");
            }
            foreach (var (name, info) in Metrics)
            {
                var values = Enumerable.Range(0, days).Select(i => DayValue(state, name, from.AddDays(i))).Where(v => v is not null).Select(v => v!.Value).ToList();
                if (values.Count == 0) continue;
                var hit = info.DailyGoal > 0 ? $", goal met {values.Count(v => v >= info.DailyGoal)}/{values.Count} days" : "";
                lines.Add($"- {name}: average {values.Average():0.#} {info.Unit} over {values.Count} logged day(s){hit}");
            }
            return lines.Count == 1 ? "No habits or health logs yet." : string.Join("\n", lines);
        }
    }

    private static bool DoneOn(HealthState state, string habit, DateOnly day) =>
        state.Entries.Any(e => e.Kind == "habit" && e.Name.Equals(habit, StringComparison.OrdinalIgnoreCase) && DateOnly.FromDateTime(e.At.LocalDateTime) == day);

    /// <summary>Consecutive days done, ending today (or yesterday, so a streak is not lost before today's check-in).</summary>
    private static int Streak(HealthState state, string habit, DateOnly today)
    {
        var day = DoneOn(state, habit, today) ? today : today.AddDays(-1);
        var streak = 0;
        while (DoneOn(state, habit, day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    private static double? DayValue(HealthState state, string metric, DateOnly day)
    {
        var entries = state.Entries.Where(e => e.Kind == "metric" && e.Name == metric && DateOnly.FromDateTime(e.At.LocalDateTime) == day).ToList();
        if (entries.Count == 0) return null;
        return Additive.Contains(metric) ? entries.Sum(e => e.Value) : entries[^1].Value;
    }

    private static void Trim(HealthState state)
    {
        if (state.Entries.Count > 20000) state.Entries.RemoveRange(0, state.Entries.Count - 20000);
    }

    private static string Clean(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("A habit name is required.");
        return name.Length > 80 ? name[..80] : name;
    }

    private HealthState Load()
    {
        if (!File.Exists(_path)) return new HealthState(new(), new());
        var state = JsonSerializer.Deserialize<HealthState>(File.ReadAllText(_path), JsonOptions);
        return new HealthState(state?.Habits ?? new(), state?.Entries ?? new());
    }

    private void Save(HealthState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    public static bool TryParseValue(string text, out double value) =>
        double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
