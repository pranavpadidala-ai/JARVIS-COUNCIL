using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Council.App.Voice;
using Council.Execution.Tools;

namespace Council.App.Health;

/// <summary>
/// Dance practice: plays a reference dance video beside the webcam and scores how closely the user's
/// pose matches (python/jarvis_runtime/dance_coach.py, MediaPipe, in Models\LocalAI\workout-venv).
/// Logs the session as workout minutes. Nothing is recorded.
/// </summary>
public sealed class DanceCoachTool : ITool
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".webm", ".avi", ".mov" };
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<string>> _run;
    private readonly HabitTracker _habits;
    private readonly Func<DateTimeOffset> _clock;

    public DanceCoachTool(Func<IReadOnlyList<string>, CancellationToken, Task<string>>? run = null, HabitTracker? habits = null, Func<DateTimeOffset>? clock = null)
    {
        _run = run ?? RunAsync;
        _habits = habits ?? new HabitTracker();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name => "dance_coach";

    public string Description =>
        "Dance practice with the webcam. Arguments: video (path to a dance video on this laptop; use file_manager find to locate it), " +
        "optional start (seconds into the video), seconds (10-600, default 60), mirror (true when the dancer faces the camera, default true). " +
        "Shows the video and the user side by side and scores the match (0-100%); reports weaker body part and moments to practise. " +
        "Only start it when the user asks. Nothing is recorded.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim().Trim('"') : "";
        var video = Arg("video");
        if (video.Length == 0) return new ToolResult(false, "video (path to a dance video file) is required.");
        video = Path.GetFullPath(Environment.ExpandEnvironmentVariables(video));
        if (!File.Exists(video)) return new ToolResult(false, $"Video not found: {video}");
        if (!VideoExtensions.Contains(Path.GetExtension(video), StringComparer.OrdinalIgnoreCase))
            return new ToolResult(false, "Use an mp4, mkv, webm, avi or mov video.");
        var start = double.TryParse(Arg("start"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Math.Max(0, s) : 0;
        var seconds = double.TryParse(Arg("seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 10, 600) : 60;
        var mirror = !bool.TryParse(Arg("mirror"), out var m) || m;

        var cli = new List<string> { "--video", video, "--start", start.ToString(CultureInfo.InvariantCulture), "--seconds", seconds.ToString(CultureInfo.InvariantCulture) };
        if (mirror) cli.Add("--mirror");

        string output;
        try { output = await _run(cli, cancellationToken); }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ToolResult(false, ex.Message);
        }

        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'));
        if (line is null) return new ToolResult(false, "The dance coach ended without a result.");
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
                return new ToolResult(false, root.TryGetProperty("error", out var e) ? e.GetString() ?? "Dance coach failed." : "Dance coach failed.");
            if (!root.GetProperty("scored").GetBoolean())
                return new ToolResult(true, "I couldn't see both you and the dancer clearly. Stand back so your whole body is in the camera, with good light.");
            var avg = root.GetProperty("average").GetDouble();
            var secs = root.GetProperty("seconds").GetInt32();
            if (secs >= 30) _habits.Log("workout", Math.Max(1, Math.Round(secs / 60.0)), _clock());
            var weaker = root.TryGetProperty("weaker_part", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
            var moments = root.GetProperty("weakest_moments").EnumerateArray()
                .Select(x => $"{TimeSpan.FromSeconds(x.GetProperty("at_seconds").GetInt32()):m\\:ss} ({x.GetProperty("score").GetInt32()}%)").ToList();
            var verdict = avg >= 80 ? "Excellent" : avg >= 65 ? "Good" : avg >= 50 ? "Getting there" : "Keep practising";
            return new ToolResult(true,
                $"{verdict}: average match {avg:0}% over {secs} s." +
                (weaker is null ? "" : $" Your {weaker} need the most work.") +
                (moments.Count > 0 ? " Practise around " + string.Join(", ", moments) + " in the video." : ""));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new ToolResult(false, "Could not read the dance result: " + ex.Message);
        }
    }

    private static async Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var python = Environment.GetEnvironmentVariable("JARVIS_WORKOUT_PYTHON") ?? Path.Combine(LocalModelPaths.ResolveRoot(), "workout-venv", "Scripts", "python.exe");
        if (!File.Exists(python)) throw new FileNotFoundException("The dance coach is not installed yet (missing " + python + ").");
        string? script = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && script is null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "python", "jarvis_runtime", "dance_coach.py");
            if (File.Exists(candidate)) script = candidate;
        }
        if (script is null) throw new FileNotFoundException("python\\jarvis_runtime\\dance_coach.py was not found.");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(script);
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the dance coach.");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        _ = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return await stdout;
    }
}
