using System.Text.Json;
using Council.Security;

namespace Council.App.Safety;

public sealed record ActionLogEntry(DateTimeOffset At, string Tool, bool Success, string Arguments, string Result);

/// <summary>
/// Local record of every tool JARVIS ran (spec section 9: "maintain an action log so the user
/// can see what JARVIS did"). One JSON line per action, secrets redacted, values shortened.
/// Logging failures never block the action itself.
/// </summary>
public sealed class ActionLog
{
    private const int MaxValueLength = 160;
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private static readonly object FileLock = new();
    private readonly string _path;

    public static ActionLog Default { get; } = new();

    public ActionLog(string? path = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("JARVIS_ACTION_LOG_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "action-log.jsonl");
    }

    public string FilePath => _path;

    public void Record(string tool, IReadOnlyDictionary<string, string> arguments, bool success, string result)
    {
        try
        {
            var args = string.Join(", ", arguments.Select(pair => pair.Key + "=" + Shorten(SecretRedactor.Redact(pair.Value))));
            var entry = new ActionLogEntry(DateTimeOffset.Now, tool, success, args, Shorten(SecretRedactor.Redact(result)));
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (FileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxFileBytes)
                    File.Move(_path, _path + ".old", overwrite: true); // keep one previous file, cap disk use
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is a convenience; never fail a user's action because it could not be written.
        }
    }

    public IReadOnlyList<ActionLogEntry> Recent(int count)
    {
        lock (FileLock)
        {
            if (!File.Exists(_path)) return Array.Empty<ActionLogEntry>();
            var entries = new List<ActionLogEntry>();
            foreach (var line in File.ReadLines(_path).Reverse().Take(Math.Clamp(count, 1, 200)))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ActionLogEntry>(line) is { } entry) entries.Add(entry);
                }
                catch (JsonException) { } // skip a partly written line
            }
            return entries;
        }
    }

    public string Describe(int count = 20)
    {
        var entries = Recent(count);
        if (entries.Count == 0) return "JARVIS has not run any tools yet.";
        return $"Last {entries.Count} action(s), newest first (full log: {_path}):\n" + string.Join("\n", entries.Select(e =>
            $"{e.At.ToLocalTime():dd MMM HH:mm:ss}  {(e.Success ? "OK  " : "FAIL")}  {e.Tool}" +
            (e.Arguments.Length > 0 ? $"  ({e.Arguments})" : "")));
    }

    private static string Shorten(string value)
    {
        var flat = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= MaxValueLength ? flat : flat[..MaxValueLength] + "…";
    }
}
