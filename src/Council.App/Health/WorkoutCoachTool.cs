using System.Diagnostics;
using System.Text.Json;
using Council.App.Voice;
using Council.Execution.Tools;

namespace Council.App.Health;

/// <summary>
/// Starts the local webcam workout coach (MediaPipe Pose), waits for it to finish, and logs the
/// workout minutes in the habits tracker. The camera is used only while the coach window is open;
/// no video is saved or sent anywhere.
/// </summary>
public sealed class WorkoutCoachTool : ITool
{
    private static readonly string[] Exercises = { "squat", "pushup", "curl", "lunge", "jumping_jack" };
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<string>> _run;
    private readonly HabitTracker _habits;
    private readonly Func<DateTimeOffset> _clock;

    public WorkoutCoachTool(Func<IReadOnlyList<string>, CancellationToken, Task<string>>? run = null,
        HabitTracker? habits = null, Func<DateTimeOffset>? clock = null)
    {
        _run = run ?? RunCoachAsync;
        _habits = habits ?? new HabitTracker();
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name => "workout_coach";

    public string Description =>
        "Webcam workout coach: counts reps and gives form tips on screen. Arguments: exercise = squat | pushup | curl | lunge | jumping_jack, " +
        "optional minutes (1-30, default 5) and target reps. Opens a camera window; the user presses Q to finish. " +
        "Only start it when the user asks for a workout. Video stays on this laptop and is not saved. " +
        "The result is logged as workout minutes in the habits tracker.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim().ToLowerInvariant() : "";
        var exercise = Arg("exercise").Replace("-", "").Replace(" ", "_") switch
        {
            "squats" => "squat", "pushups" or "push_up" or "push_ups" => "pushup", "curls" or "bicep_curl" => "curl",
            "lunges" => "lunge", "jumping_jacks" or "jumpingjack" => "jumping_jack", var e => e
        };
        if (!Exercises.Contains(exercise))
            return new ToolResult(false, "exercise must be squat, pushup, curl, lunge or jumping_jack.");
        var minutes = double.TryParse(Arg("minutes"), out var m) ? Math.Clamp(m, 1, 30) : 5;
        var target = int.TryParse(Arg("target"), out var t) ? Math.Clamp(t, 0, 500) : 0;

        string output;
        try
        {
            output = await _run(new[] { "--exercise", exercise, "--minutes", minutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--target", target.ToString() }, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ToolResult(false, ex.Message);
        }

        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'));
        if (line is null) return new ToolResult(false, "The workout coach ended without a result.");
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
                return new ToolResult(false, root.TryGetProperty("error", out var err) ? err.GetString() ?? "Workout coach failed." : "Workout coach failed.");
            var reps = root.GetProperty("reps").GetInt32();
            var mins = root.GetProperty("minutes").GetDouble();
            var seen = root.TryGetProperty("body_seen", out var b) && b.GetBoolean();
            if (mins >= 0.5) _habits.Log("workout", Math.Round(mins), _clock());
            var name = exercise.Replace('_', ' ');
            return new ToolResult(true, seen
                ? $"Workout done: {reps} {name} rep(s) in {mins:0.#} min{(target > 0 ? $" (target {target})" : "")}. Logged as workout time."
                : "The camera could not see your body. Step back so your whole body is in the frame and try again.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return new ToolResult(false, "Could not read the workout result: " + ex.Message);
        }
    }

    private static async Task<string> RunCoachAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var root = LocalModelPaths.ResolveRoot();
        var python = Environment.GetEnvironmentVariable("JARVIS_WORKOUT_PYTHON") ?? Path.Combine(root, "workout-venv", "Scripts", "python.exe");
        if (!File.Exists(python))
            throw new FileNotFoundException("The workout coach is not installed yet (missing " + python + ").");
        var script = FindScript() ?? throw new FileNotFoundException("python\\jarvis_runtime\\workout_coach.py was not found.");

        var start = new ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(script);
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the workout coach.");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        await stderr;
        return await stdout;
    }

    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "python", "jarvis_runtime", "workout_coach.py");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
