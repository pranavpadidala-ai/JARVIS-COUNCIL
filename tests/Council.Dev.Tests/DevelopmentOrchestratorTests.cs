using Council.Dev.Execution;
using Council.Dev.Git;
using Council.Dev.Loop;
using Council.Dev.Tasks;

using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

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

        var taskRunner =
            new Council.Dev.Runner.TaskRunner(taskManager);

        var loop =
            new DevelopmentLoop(
                taskManager,
                taskRunner);

        var executor =
            new FakeDevelopmentExecutor(
                DevelopmentExecutionResult.Completed(
                    "Development succeeded."));

        var gitCheckpointManager =
            new FakeGitCheckpointManager();

        var orchestrator =
            new DevelopmentOrchestrator(
                loop,
                executor,
                gitCheckpointManager);

        var result =
            await orchestrator.ExecuteAsync(task);

        Assert.True(result.Success);

        Assert.Contains(
            "Development succeeded.",
            result.Message);

        Assert.Equal(
            1,
            executor.CallCount);

        Assert.Equal(
            1,
            gitCheckpointManager.CreateCheckpointCallCount);

        Assert.Equal(
            1,
            gitCheckpointManager.CommitCallCount);

        Assert.Equal(
            0,
            gitCheckpointManager.RollbackCallCount);

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

        var taskRunner =
            new Council.Dev.Runner.TaskRunner(taskManager);

        var loop =
            new DevelopmentLoop(
                taskManager,
                taskRunner);

        var executor =
            new FakeDevelopmentExecutor(
                DevelopmentExecutionResult.Failed(
                    "First failure."),
                DevelopmentExecutionResult.Failed(
                    "Second failure."),
                DevelopmentExecutionResult.Completed(
                    "Recovered."));

        var gitCheckpointManager =
            new FakeGitCheckpointManager();

        var orchestrator =
            new DevelopmentOrchestrator(
                loop,
                executor,
                gitCheckpointManager);

        var result =
            await orchestrator.ExecuteAsync(
                task,
                maxAttempts: 3);

        Assert.True(result.Success);

        Assert.Contains(
            "Recovered.",
            result.Message);

        Assert.Equal(
            3,
            executor.CallCount);

        Assert.Equal(
            3,
            task.AttemptCount);

        Assert.Equal(
            3,
            gitCheckpointManager.CreateCheckpointCallCount);

        Assert.Equal(
            1,
            gitCheckpointManager.CommitCallCount);

        Assert.Equal(
            2,
            gitCheckpointManager.RollbackCallCount);

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

        var taskRunner =
            new Council.Dev.Runner.TaskRunner(taskManager);

        var loop =
            new DevelopmentLoop(
                taskManager,
                taskRunner);

        var executor =
            new FakeDevelopmentExecutor(
                DevelopmentExecutionResult.Failed(
                    "Failure 1."),
                DevelopmentExecutionResult.Failed(
                    "Failure 2."),
                DevelopmentExecutionResult.Failed(
                    "Failure 3."));

        var gitCheckpointManager =
            new FakeGitCheckpointManager();

        var orchestrator =
            new DevelopmentOrchestrator(
                loop,
                executor,
                gitCheckpointManager);

        var result =
            await orchestrator.ExecuteAsync(
                task,
                maxAttempts: 3);

        Assert.False(result.Success);

        Assert.Contains(
            "Failure 3.",
            result.Message);

        Assert.Equal(
            3,
            executor.CallCount);

        Assert.Equal(
            3,
            task.AttemptCount);

        Assert.Equal(
            3,
            gitCheckpointManager.CreateCheckpointCallCount);

        Assert.Equal(
            0,
            gitCheckpointManager.CommitCallCount);

        Assert.Equal(
            3,
            gitCheckpointManager.RollbackCallCount);

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

        var taskRunner =
            new Council.Dev.Runner.TaskRunner(taskManager);

        var loop =
            new DevelopmentLoop(
                taskManager,
                taskRunner);

        var executor =
            new FakeDevelopmentExecutor(
                DevelopmentExecutionResult.Completed());

        var gitCheckpointManager =
            new FakeGitCheckpointManager();

        var orchestrator =
            new DevelopmentOrchestrator(
                loop,
                executor,
                gitCheckpointManager);

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orchestrator.ExecuteAsync(
                task,
                cancellationToken:
                    cancellationSource.Token));

        Assert.Equal(
            0,
            executor.CallCount);

        Assert.Equal(
            0,
            gitCheckpointManager.CreateCheckpointCallCount);
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
            _results =
                new Queue<DevelopmentExecutionResult>(
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

    private sealed class FakeGitCheckpointManager
        : IGitCheckpointManager
    {
        private int _checkpointNumber;

        public int CreateCheckpointCallCount
        {
            get;
            private set;
        }

        public int CommitCallCount
        {
            get;
            private set;
        }

        public int RollbackCallCount
        {
            get;
            private set;
        }

        public Task<GitCheckpointResult> CreateCheckpointAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CreateCheckpointCallCount++;

            _checkpointNumber++;

            return Task.FromResult(
                GitCheckpointResult.Completed(
                    $"Checkpoint {_checkpointNumber} created.",
                    new GitCheckpoint(
                        $"fake-checkpoint-{_checkpointNumber}")));
        }

        public Task<GitCheckpointResult> CommitAsync(
            string message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CommitCallCount++;

            return Task.FromResult(
                GitCheckpointResult.Completed(
                    $"Committed: {message}"));
        }

        public Task<GitCheckpointResult> RollbackAsync(
            GitCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RollbackCallCount++;

            return Task.FromResult(
                GitCheckpointResult.Completed(
                    $"Rolled back to {checkpoint.CommitSha}."));
        }
    }
}
