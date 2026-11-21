using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Council.App.Reminders;

public sealed record Reminder(string Id, string Text, DateTimeOffset DueAt, DateTimeOffset CreatedAt, bool Delivered);

/// <summary>
/// Small local reminder list (JSON file, atomic replace). The desktop app polls
/// <see cref="ClaimDue"/> and shows a tray notification for each due reminder.
/// </summary>
public sealed class ReminderStore
{
    public const int MaxReminders = 500;
    public const int MaxTextLength = 500;
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public ReminderStore(string? path = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("JARVIS_REMINDERS_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "reminders.json");
    }

    public string FilePath => _path;

    public Reminder Add(string text, DateTimeOffset dueAt, DateTimeOffset now)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("Reminder text is required.");
        if (text.Length > MaxTextLength) throw new ArgumentException($"Reminder text is limited to {MaxTextLength} characters.");
        if (dueAt <= now) throw new ArgumentException("The reminder time is in the past.");

        lock (FileLock)
        {
            var items = Load();
            if (items.Count(r => !r.Delivered) >= MaxReminders)
                throw new InvalidOperationException($"There are already {MaxReminders} pending reminders.");
            items.RemoveAll(r => r.Delivered && r.DueAt < now.AddDays(-30)); // keep the file small
            var reminder = new Reminder(Guid.NewGuid().ToString("N")[..8], text, dueAt, now, false);
            items.Add(reminder);
            Save(items);
            return reminder;
        }
    }

    public IReadOnlyList<Reminder> ListPending()
    {
        lock (FileLock)
            return Load().Where(r => !r.Delivered).OrderBy(r => r.DueAt).ToList();
    }

    public bool Cancel(string id)
    {
        lock (FileLock)
        {
            var items = Load();
            var removed = items.RemoveAll(r => !r.Delivered && string.Equals(r.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save(items);
            return removed;
        }
    }

    /// <summary>Marks every reminder due at or before <paramref name="now"/> as delivered and returns them once.</summary>
    public IReadOnlyList<Reminder> ClaimDue(DateTimeOffset now)
    {
        lock (FileLock)
        {
            var items = Load();
            var due = items.Where(r => !r.Delivered && r.DueAt <= now).OrderBy(r => r.DueAt).ToList();
            if (due.Count == 0) return due;
            for (var i = 0; i < items.Count; i++)
                if (!items[i].Delivered && items[i].DueAt <= now)
                    items[i] = items[i] with { Delivered = true };
            Save(items);
            return due;
        }
    }

    private List<Reminder> Load()
    {
        if (!File.Exists(_path)) return new List<Reminder>();
        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json)) return new List<Reminder>();
        try
        {
            return JsonSerializer.Deserialize<List<Reminder>>(json, JsonOptions) ?? new List<Reminder>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The reminder file is damaged and was left untouched: {_path}. {ex.Message}");
        }
    }

    private void Save(List<Reminder> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>Parses the reminder times people actually type or say.</summary>
public static class ReminderTimeParser
{
    private static readonly Regex Relative = new(
        @"^in\s+(?<n>\d+(?:\.\d+)?)\s*(?<unit>m|min|mins|minute|minutes|h|hr|hrs|hour|hours|d|day|days)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DayAndTime = new(
        @"^(?<day>today|tomorrow)(?:\s+at)?\s+(?<time>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] TimeFormats = { "H:mm", "HH:mm", "h:mm tt", "h tt", "htt", "h:mmtt" };
    private static readonly string[] DateTimeFormats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm", "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm", "yyyy-MM-dd" };

    /// <summary>Accepts "in 20 minutes", "in 2 hours", "tomorrow 9:00", "today 6:30 pm", "18:30" or "2026-11-22 18:30" (local time).</summary>
    public static DateTimeOffset Parse(string when, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var text = Regex.Replace((when ?? "").Trim(), @"\s+", " ");
        if (text.Length == 0) throw new ArgumentException("A reminder time is required, for example 'in 20 minutes' or 'tomorrow 9:00'.");
        var localNow = TimeZoneInfo.ConvertTime(now, zone);

        var relative = Relative.Match(text);
        if (relative.Success)
        {
            var amount = double.Parse(relative.Groups["n"].Value, CultureInfo.InvariantCulture);
            var unit = relative.Groups["unit"].Value.ToLowerInvariant();
            var span = unit.StartsWith('m') ? TimeSpan.FromMinutes(amount) : unit.StartsWith('h') ? TimeSpan.FromHours(amount) : TimeSpan.FromDays(amount);
            if (span <= TimeSpan.Zero || span > TimeSpan.FromDays(366)) throw new ArgumentException("Reminders can be set up to one year ahead.");
            return now + span;
        }

        var dayMatch = DayAndTime.Match(text);
        if (dayMatch.Success)
        {
            var time = ParseTimeOfDay(dayMatch.Groups["time"].Value);
            var date = localNow.Date.AddDays(dayMatch.Groups["day"].Value.Equals("tomorrow", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            return ToZone(date + time, zone);
        }

        if (TryParseTimeOfDay(text, out var onlyTime))
        {
            var candidate = localNow.Date + onlyTime;
            if (candidate <= localNow.DateTime) candidate = candidate.AddDays(1); // "18:30" after 18:30 means tomorrow
            return ToZone(candidate, zone);
        }

        if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var absolute))
            return ToZone(absolute, zone);

        throw new ArgumentException($"Could not understand the time '{when}'. Use 'in 20 minutes', 'tomorrow 9:00', '18:30' or '2026-11-22 18:30'.");
    }

    private static TimeSpan ParseTimeOfDay(string value) =>
        TryParseTimeOfDay(value, out var time) ? time : throw new ArgumentException($"Could not understand the time of day '{value}'.");

    private static bool TryParseTimeOfDay(string value, out TimeSpan time)
    {
        var normalized = value.Trim().ToUpperInvariant().Replace(".", "");
        if (DateTime.TryParseExact(normalized, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            time = parsed.TimeOfDay;
            return true;
        }
        time = default;
        return false;
    }

    private static DateTimeOffset ToZone(DateTime local, TimeZoneInfo zone) =>
        new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone.GetUtcOffset(local));
}
