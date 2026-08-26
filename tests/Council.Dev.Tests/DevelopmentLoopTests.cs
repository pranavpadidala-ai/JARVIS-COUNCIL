using Council.Dev.Loop;
using Council.Dev.Runner;
using Council.Dev.Tasks;
using Xunit;
using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Tests;

public class DevelopmentLoopTests
{
    [Fact]
    public async Task Successful_first_attempt_completes_task()
    {
        var (manager, task) = await CreateTaskAsync();
        var runner = new TaskRunner(manager);
        var loop = new DevelopmentLoop(manager, runner);

        var executions = 0;

        await loop.RunAsync(
            task,
            (currentTask, token) =>
            {
                executions++;
                return Task.FromResult(
                    DevelopmentCycleResult.Passed("Success"));
            });

        Assert.Equal(1, executions);
        Assert.Equal(1, task.AttemptCount);
        Assert.Equal(CouncilTaskStatus.Completed, task.Status);
        Assert.Equal("Success", task.CurrentStep);
    }

    [Fact]
    public async Task Failed_attempt_is_retried_and_second_attempt_can_succeed()
    {
        var (manager, task) = await CreateTaskAsync();
        var runner = new TaskRunner(manager);
        var loop = new DevelopmentLoop(manager, runner);

        var executions = 0;

        await loop.RunAsync(
            task,
            (currentTask, token) =>
            {
                executions++;

                return Task.FromResult(
                    executions == 1
                        ? DevelopmentCycleResult.Failed("First attempt failed")
                        : DevelopmentCycleResult.Passed("Second attempt succeeded"));
            });

        Assert.Equal(2, executions);
        Assert.Equal(2, task.AttemptCount);
        Assert.Equal(CouncilTaskStatus.Completed, task.Status);
        Assert.Equal("Second attempt succeeded", task.CurrentStep);
    }

    [Fact]
    public async Task All_failed_attempts_mark_task_failed()
    {
        var (manager, task) = await CreateTaskAsync();
        var runner = new TaskRunner(manager);
        var loop = new DevelopmentLoop(manager, runner);

        var executions = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loop.RunAsync(
                task,
                (currentTask, token) =>
                {
                    executions++;

                    return Task.FromResult(
                        DevelopmentCycleResult.Failed("Still failing"));
                },
                maxAttempts: 3));

        Assert.Contains(
            "Development failed after 3 attempts",
            exception.Message);

        Assert.Equal(3, executions);
        Assert.Equal(3, task.AttemptCount);
        Assert.Equal(CouncilTaskStatus.Failed, task.Status);
        Assert.Equal("Still failing", task.CurrentStep);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var (manager, task) = await CreateTaskAsync();
        var runner = new TaskRunner(manager);
        var loop = new DevelopmentLoop(manager, runner);

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loop.RunAsync(
                task,
                (currentTask, token) =>
                {
                    return Task.FromResult(
                        DevelopmentCycleResult.Passed());
                },
                cancellationToken: cancellationSource.Token));

        Assert.Equal(CouncilTaskStatus.Pending, task.Status);
    }

    [Fact]
    public async Task Zero_max_attempts_is_rejected()
    {
        var (manager, task) = await CreateTaskAsync();
        var runner = new TaskRunner(manager);
        var loop = new DevelopmentLoop(manager, runner);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => loop.RunAsync(
                task,
                (currentTask, token) =>
                    Task.FromResult(
                        DevelopmentCycleResult.Passed()),
                maxAttempts: 0));
    }

    [Fact]
    public async Task Attempt_count_matches_number_of_executions()
    {
        var (manager, task) = await CreateTaskAsync();
        var runner = new TaskRunner(manager);
        var loop = new DevelopmentLoop(manager, runner);

        var executions = 0;

        await loop.RunAsync(
            task,
            (currentTask, token) =>
            {
                executions++;

                return Task.FromResult(
                    executions < 3
                        ? DevelopmentCycleResult.Failed("Retry")
                        : DevelopmentCycleResult.Passed("Done"));
            },
            maxAttempts: 3);

        Assert.Equal(executions, task.AttemptCount);
        Assert.Equal(3, task.AttemptCount);
        Assert.Equal(CouncilTaskStatus.Completed, task.Status);
    }

    private static async Task<(TaskManager Manager, DevelopmentTask Task)>
        CreateTaskAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "JARVIS-DevelopmentLoopTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(
            directory,
            "tasks.json");

        var store = new TaskStore(filePath);
        var manager = new TaskManager(store);

        var task = await manager.CreateAsync(
            "Test development task",
            "Test development task description");

        return (manager, task);
    }
}



