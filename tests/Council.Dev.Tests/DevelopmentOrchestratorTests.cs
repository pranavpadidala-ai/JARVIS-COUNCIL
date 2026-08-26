using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;
using Council.Dev.Execution;
using Council.Dev.Loop;
using Council.Dev.Tasks;

namespace Council.Dev.Tests;

public class DevelopmentOrchestratorTests
{
    [Fact]
    public async Task Successful_execution_returns_completed()
    {
        var taskStore = CreateStore();
        var taskManager = new TaskManager(taskStore);

        var task = await taskManager.CreateAsync(
            "Test task",
            "Test description");

        var taskRunner = new Council.Dev.Runner.TaskRunner(taskManager);
        var loop = new DevelopmentLoop(taskManager, taskRunner);

        var executor = new FakeDevelopmentExecutor(
            DevelopmentExecutionResult.Completed(
                "Development succeeded."));

        var orchestrator = new DevelopmentOrchestrator(
            loop,
            executor);

        var result = await orchestrator.ExecuteAsync(task);

        Assert.True(result.Success);
        Assert.Equal(
            "Development succeeded.",
            result.Message);

        Assert.Equal(1, executor.CallCount);
        Assert.Equal(
            CouncilTaskStatus.Completed,
            task.Status);
    }

    [Fact]
    public async Task Failed_execution_is_retried()
    {
        var taskStore = CreateStore();
        var taskManager = new TaskManager(taskStore);

        var task = await taskManager.CreateAsync(
            "Test task",
            "Test description");

        var taskRunner = new Council.Dev.Runner.TaskRunner(taskManager);
        var loop = new DevelopmentLoop(taskManager, taskRunner);

        var executor = new FakeDevelopmentExecutor(
            DevelopmentExecutionResult.Failed("First failure."),
            DevelopmentExecutionResult.Failed("Second failure."),
            DevelopmentExecutionResult.Completed("Recovered."));

        var orchestrator = new DevelopmentOrchestrator(
            loop,
            executor);

        var result = await orchestrator.ExecuteAsync(
            task,
            maxAttempts: 3);

        Assert.True(result.Success);
        Assert.Equal("Recovered.", result.Message);

        Assert.Equal(3, executor.CallCount);
        Assert.Equal(3, task.AttemptCount);
        Assert.Equal(
            CouncilTaskStatus.Completed,
            task.Status);
    }

    [Fact]
    public async Task Failure_after_max_attempts_returns_failed()
    {
        var taskStore = CreateStore();
        var taskManager = new TaskManager(taskStore);

        var task = await taskManager.CreateAsync(
            "Test task",
            "Test description");

        var taskRunner = new Council.Dev.Runner.TaskRunner(taskManager);
        var loop = new DevelopmentLoop(taskManager, taskRunner);

        var executor = new FakeDevelopmentExecutor(
            DevelopmentExecutionResult.Failed("Failure 1."),
            DevelopmentExecutionResult.Failed("Failure 2."),
            DevelopmentExecutionResult.Failed("Failure 3."));

        var orchestrator = new DevelopmentOrchestrator(
            loop,
            executor);

        var result = await orchestrator.ExecuteAsync(
            task,
            maxAttempts: 3);

        Assert.False(result.Success);
        Assert.Contains(
            "Failure 3.",
            result.Message);

        Assert.Equal(3, executor.CallCount);
        Assert.Equal(3, task.AttemptCount);
        Assert.Equal(
            CouncilTaskStatus.Failed,
            task.Status);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var taskStore = CreateStore();
        var taskManager = new TaskManager(taskStore);

        var task = await taskManager.CreateAsync(
            "Test task",
            "Test description");

        var taskRunner = new Council.Dev.Runner.TaskRunner(taskManager);
        var loop = new DevelopmentLoop(taskManager, taskRunner);

        var executor = new FakeDevelopmentExecutor(
            DevelopmentExecutionResult.Completed());

        var orchestrator = new DevelopmentOrchestrator(
            loop,
            executor);

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orchestrator.ExecuteAsync(
                task,
                cancellationToken:
                    cancellationSource.Token));
    }

    private static TaskStore CreateStore()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "JARVIS",
            Guid.NewGuid().ToString("N"),
            "tasks.json");

        return new TaskStore(path);
    }

    private sealed class FakeDevelopmentExecutor
        : IDevelopmentExecutor
    {
        private readonly Queue<DevelopmentExecutionResult> _results;

        public int CallCount { get; private set; }

        public FakeDevelopmentExecutor(
            params DevelopmentExecutionResult[] results)
        {
            _results = new Queue<DevelopmentExecutionResult>(
                results);
        }

        public Task<DevelopmentExecutionResult> ExecuteAsync(
            DevelopmentTask task,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            if (_results.Count == 0)
            {
                throw new InvalidOperationException(
                    "No fake result configured.");
            }

            return Task.FromResult(
                _results.Dequeue());
        }
    }
}


