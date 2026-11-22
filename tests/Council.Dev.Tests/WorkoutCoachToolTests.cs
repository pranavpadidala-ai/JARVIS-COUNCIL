using Council.App.Health;
using Xunit;

namespace Council.Dev.Tests;

public sealed class WorkoutCoachToolTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-workout-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly DateTimeOffset _now = new(2026, 11, 22, 7, 0, 0, TimeSpan.FromHours(5.5));

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task LogsTheWorkoutFromTheCoachResult()
    {
        IReadOnlyList<string>? seenArgs = null;
        var habits = new HabitTracker(_path);
        var tool = new WorkoutCoachTool((args, _) =>
        {
            seenArgs = args;
            return Task.FromResult("loading...\n{\"ok\": true, \"exercise\": \"squat\", \"reps\": 18, \"minutes\": 4.6, \"body_seen\": true}\n");
        }, habits, () => _now);

        var result = await tool.ExecuteAsync(new() { ["exercise"] = "Squats", ["minutes"] = "5", ["target"] = "20" });
        Assert.True(result.Success);
        Assert.Contains("18 squat rep(s)", result.Message);
        Assert.Equal("squat", seenArgs![1]);
        Assert.Equal(5, habits.TodayValue("workout", _now));
    }

    [Fact]
    public async Task ExplainsCameraAndInputProblems()
    {
        var noBody = new WorkoutCoachTool((_, _) => Task.FromResult("{\"ok\": true, \"reps\": 0, \"minutes\": 1.0, \"body_seen\": false}"),
            new HabitTracker(_path), () => _now);
        Assert.Contains("could not see your body", (await noBody.ExecuteAsync(new() { ["exercise"] = "pushup" })).Message);

        var busy = new WorkoutCoachTool((_, _) => Task.FromResult("{\"ok\": false, \"error\": \"Could not open the webcam.\"}"),
            new HabitTracker(_path), () => _now);
        Assert.False((await busy.ExecuteAsync(new() { ["exercise"] = "curl" })).Success);

        Assert.False((await busy.ExecuteAsync(new() { ["exercise"] = "yoga" })).Success);
    }
}
