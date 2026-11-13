using Council.Dev.Recovery;

namespace Council.Dev.Tasks;

public sealed class TaskManager
{
    private readonly TaskStore _store;
    private readonly TaskCheckpointStore? _checkpointStore;
    private readonly List<DevelopmentTask> _tasks = [];

    public TaskManager(TaskStore store, TaskCheckpointStore? checkpointStore = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _checkpointStore = checkpointStore;
    }

    public IReadOnlyList<DevelopmentTask> Tasks => _tasks;

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var loadedTasks = await _store.LoadAsync(cancellationToken);

        _tasks.Clear();
        _tasks.AddRange(loadedTasks);

        var recoveredInterruptedTask = false;
        foreach (var task in _tasks.Where(t => t.Status == TaskStatus.Running))
        {
            var checkpoint = _checkpointStore is null
                ? null
                : await _checkpointStore.LoadAsync(task.Id, cancellationToken);
            task.Status = TaskStatus.Paused;
            task.CurrentStep = checkpoint is null
                ? "Recovered after an unexpected shutdown. Review the task state before resuming."
                : checkpoint.NextAction;
            if (checkpoint is not null)
            {
                task.CompletedSteps = checkpoint.CompletedSteps.ToList();
                task.ProviderRoute = checkpoint.ProviderRoute;
                task.ModelAlias = checkpoint.ModelAlias;
                task.GitCommitSha = checkpoint.GitCommitSha;
                task.GitWorkingTreeStatus = checkpoint.GitWorkingTreeStatus;
                task.BuildLogPath = checkpoint.BuildLogPath;
                task.TestLogPath = checkpoint.TestLogPath;
                task.FailureClassification = checkpoint.FailureClassification;
                task.IdempotencyKey = checkpoint.IdempotencyKey;
                task.AttemptCount = Math.Max(task.AttemptCount, checkpoint.RetryCount);
            }
            task.UpdatedAtUtc = DateTime.UtcNow;
            recoveredInterruptedTask = true;
        }

        if (recoveredInterruptedTask)
            await PersistAsync(cancellationToken);
    }

    public async Task<DevelopmentTask> CreateAsync(
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Task title is required.",
                nameof(title));
        }

        if (_tasks.Count >= 500)
            throw new InvalidOperationException("JARVIS can retain at most 500 development tasks.");

        var task = new DevelopmentTask
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Status = TaskStatus.Pending,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _tasks.Add(task);
        try
        {
            await PersistAsync(cancellationToken);
            return task;
        }
        catch
        {
            _tasks.Remove(task);
            throw;
        }
    }

    public async Task<bool> UpdateStatusAsync(
        Guid taskId,
        TaskStatus status,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId);

        if (task is null)
        {
            return false;
        }

        var previousStatus = task.Status;
        var previousUpdatedAt = task.UpdatedAtUtc;
        task.Status = status;
        task.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await PersistAsync(cancellationToken);
            return true;
        }
        catch
        {
            task.Status = previousStatus;
            task.UpdatedAtUtc = previousUpdatedAt;
            throw;
        }
    }

    public async Task<bool> UpdateStepAsync(
        Guid taskId,
        string currentStep,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId);

        if (task is null)
        {
            return false;
        }

        var previousStep = task.CurrentStep;
        var previousCompletedSteps = task.CompletedSteps.ToList();
        var previousUpdatedAt = task.UpdatedAtUtc;
        if (!string.IsNullOrWhiteSpace(previousStep) &&
            !string.Equals(previousStep, currentStep?.Trim(), StringComparison.Ordinal))
            task.CompletedSteps.Add(previousStep);
        task.CurrentStep = currentStep?.Trim() ?? string.Empty;
        task.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await PersistAsync(cancellationToken);
            return true;
        }
        catch
        {
            task.CurrentStep = previousStep;
            task.CompletedSteps = previousCompletedSteps;
            task.UpdatedAtUtc = previousUpdatedAt;
            throw;
        }
    }

    public async Task<bool> IncrementAttemptAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId);

        if (task is null)
        {
            return false;
        }

        var previousAttemptCount = task.AttemptCount;
        var previousUpdatedAt = task.UpdatedAtUtc;
        task.AttemptCount++;
        task.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await PersistAsync(cancellationToken);
            return true;
        }
        catch
        {
            task.AttemptCount = previousAttemptCount;
            task.UpdatedAtUtc = previousUpdatedAt;
            throw;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _store.SaveAsync(_tasks, cancellationToken);
        if (_checkpointStore is null) return;

        foreach (var task in _tasks.Where(t => t.Status is TaskStatus.Running or TaskStatus.Paused or TaskStatus.Failed))
        {
            await _checkpointStore.SaveAsync(new TaskCheckpoint
            {
                TaskId = task.Id,
                Objective = string.IsNullOrWhiteSpace(task.Description)
                    ? task.Title
                    : task.Title + Environment.NewLine + task.Description,
                CompletedSteps = task.CompletedSteps.ToArray(),
                NextAction = task.CurrentStep,
                ProviderRoute = task.ProviderRoute,
                ModelAlias = task.ModelAlias,
                GitCommitSha = task.GitCommitSha,
                GitWorkingTreeStatus = task.GitWorkingTreeStatus,
                BuildLogPath = task.BuildLogPath,
                TestLogPath = task.TestLogPath,
                RetryCount = task.AttemptCount,
                FailureClassification = task.FailureClassification,
                IdempotencyKey = task.IdempotencyKey,
                CreatedAtUtc = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    public DevelopmentTask? Find(Guid taskId)
    {
        return _tasks.FirstOrDefault(task => task.Id == taskId);
    }

    public DevelopmentTask? GetActiveTask()
    {
        return _tasks.FirstOrDefault(task =>
            task.Status == TaskStatus.Running ||
            task.Status == TaskStatus.Paused);
    }
}
