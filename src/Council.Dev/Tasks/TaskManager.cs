using System.Text.Json;
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    /// <summary>Refreshes the in-memory view from the shared cross-process task store.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var loaded = await _store.LoadUnlockedAsync(cancellationToken);
        ReplaceTasks(loaded);
    }

    /// <summary>
    /// Called once by the single background host at startup. Any task left Running,
    /// or claimed but not started, is paused and requires a fresh approval.
    /// </summary>
    public async Task<int> RecoverInterruptedTasksAsync(CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var recoveredTasks = new List<DevelopmentTask>();

        foreach (var task in latest)
        {
            var wasRunning = task.Status == TaskStatus.Running;
            var wasClaimedButNotStarted = task.Status == TaskStatus.Pending && task.ApprovalConsumedAtUtc.HasValue;
            if (!wasRunning && !wasClaimedButNotStarted)
                continue;

            TaskCheckpoint? checkpoint = null;
            if (_checkpointStore is not null)
            {
                try { checkpoint = await _checkpointStore.LoadAsync(task.Id, cancellationToken); }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    task.FailureClassification = "checkpoint_unreadable";
                }
            }

            task.Status = TaskStatus.Paused;
            task.IsApproved = false;
            task.ApprovalConsumedAtUtc = null;
            task.UpdatedAtUtc = DateTime.UtcNow;
            if (checkpoint is null)
            {
                task.CurrentStep = task.FailureClassification == "checkpoint_unreadable"
                    ? "Recovered after interruption, but the checkpoint could not be read. Inspect repository state before resuming."
                    : "Interrupted by host shutdown; review task state and grant fresh approval before resuming.";
            }
            else
            {
                task.CompletedSteps = checkpoint.CompletedSteps.ToList();
                task.CurrentStep = string.IsNullOrWhiteSpace(checkpoint.NextAction)
                    ? "Recovered from checkpoint. Inspect repository state before resuming."
                    : checkpoint.NextAction;
                task.ProviderRoute = checkpoint.ProviderRoute;
                task.ModelAlias = checkpoint.ModelAlias;
                task.GitCommitSha = checkpoint.GitCommitSha;
                task.GitWorkingTreeStatus = checkpoint.GitWorkingTreeStatus;
                task.BuildLogPath = checkpoint.BuildLogPath;
                task.TestLogPath = checkpoint.TestLogPath;
                task.FailureClassification = checkpoint.FailureClassification;
                task.IdempotencyKey = checkpoint.IdempotencyKey;
                task.Artifacts = checkpoint.Artifacts.ToList();
                task.AttemptCount = Math.Max(task.AttemptCount, checkpoint.RetryCount);
            }
            recoveredTasks.Add(task);
        }

        if (recoveredTasks.Count > 0)
        {
            await _store.SaveUnlockedAsync(latest, cancellationToken);
            if (_checkpointStore is not null)
                foreach (var task in recoveredTasks)
                    await SaveCheckpointAsync(task, cancellationToken);
        }
        ReplaceTasks(latest);
        return recoveredTasks.Count;
    }

    public async Task<DevelopmentTask> CreateAsync(
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Task title is required.", nameof(title));

        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        if (latest.Count >= 500)
            throw new InvalidOperationException("JARVIS can retain at most 500 development tasks.");

        var task = new DevelopmentTask
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Status = TaskStatus.Pending,
            IsApproved = false,
            UpdatedAtUtc = DateTime.UtcNow
        };

        latest.Add(task);
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        ReplaceTasks(latest);
        return task;
    }

    public async Task<bool> ApproveAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null || task.Status is TaskStatus.Completed or TaskStatus.Running ||
            task.ApprovalConsumedAtUtc.HasValue)
            return false;

        task.Status = TaskStatus.Pending;
        task.IsApproved = true;
        task.ApprovedAtUtc = DateTime.UtcNow;
        task.ApprovalConsumedAtUtc = null;
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.CurrentStep = "Approved by user; waiting for the local development agent.";
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        await SaveCheckpointAsync(task, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    public async Task<bool> RevokeApprovalAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null || task.Status == TaskStatus.Running ||
            task.ApprovalConsumedAtUtc.HasValue)
            return false;

        task.IsApproved = false;
        task.ApprovalConsumedAtUtc = null;
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.CurrentStep = "Background approval revoked by user.";
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    /// <summary>Atomically consumes the next one-shot user approval for the background host.</summary>
    public async Task<DevelopmentTask?> ClaimNextApprovedTaskAsync(
        CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest
            .Where(item => item.IsApproved &&
                item.ApprovalConsumedAtUtc is null &&
                item.Status == TaskStatus.Pending)
            .OrderBy(item => item.ApprovedAtUtc ?? item.CreatedAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .FirstOrDefault();

        if (task is null)
        {
            ReplaceTasks(latest);
            return null;
        }

        task.ApprovalConsumedAtUtc = DateTime.UtcNow;
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.CurrentStep = "User approval claimed by the local background agent.";
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        ReplaceTasks(latest);
        return task;
    }

    public async Task<bool> UpdateStatusAsync(
        Guid taskId,
        TaskStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
            return false;

        task.Status = status;
        if (status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Paused)
        {
            // A claimed one-shot approval is spent when its run finishes or is
            // interrupted. The user must explicitly approve a retry.
            task.IsApproved = false;
            task.ApprovalConsumedAtUtc = null;
        }
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    public async Task<bool> UpdateStepAsync(
        Guid taskId,
        string currentStep,
        CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
            return false;

        task.CurrentStep = currentStep?.Trim() ?? string.Empty;
        if (task.CurrentStep.Length > 8000)
            task.CurrentStep = task.CurrentStep[..8000] + " [truncated]";
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        if (task.Status is TaskStatus.Running or TaskStatus.Paused or TaskStatus.Failed)
            await SaveCheckpointAsync(task, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    public async Task<bool> CompleteStepAsync(
        Guid taskId, string completedStep, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(completedStep))
            throw new ArgumentException("A completed step description is required.", nameof(completedStep));
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null) return false;
        var normalized = completedStep.Trim();
        if (normalized.Length > 2000) normalized = normalized[..2000] + " [truncated]";
        if (!task.CompletedSteps.Contains(normalized, StringComparer.Ordinal))
        {
            task.CompletedSteps.Add(normalized);
            if (task.CompletedSteps.Count > 100)
                task.CompletedSteps.RemoveRange(0, task.CompletedSteps.Count - 100);
        }
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        if (task.Status is TaskStatus.Running or TaskStatus.Paused or TaskStatus.Failed)
            await SaveCheckpointAsync(task, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    public async Task<bool> IncrementAttemptAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
            return false;

        task.AttemptCount++;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        if (task.Status is TaskStatus.Running or TaskStatus.Paused or TaskStatus.Failed)
            await SaveCheckpointAsync(task, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    public async Task<bool> UpdateExecutionContextAsync(
        Guid taskId, string? providerRoute, string? modelAlias, string? gitCommitSha,
        string? gitWorkingTreeStatus, CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null) return false;
        task.ProviderRoute = providerRoute ?? task.ProviderRoute;
        task.ModelAlias = modelAlias ?? task.ModelAlias;
        task.GitCommitSha = gitCommitSha ?? task.GitCommitSha;
        task.GitWorkingTreeStatus = gitWorkingTreeStatus ?? task.GitWorkingTreeStatus;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        if (task.Status is TaskStatus.Running or TaskStatus.Paused or TaskStatus.Failed)
            await SaveCheckpointAsync(task, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    public async Task<bool> UpdateValidationReferencesAsync(
        Guid taskId, string? buildLogPath, string? testLogPath, string? failureClassification,
        CancellationToken cancellationToken = default)
    {
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null) return false;
        task.BuildLogPath = buildLogPath ?? task.BuildLogPath;
        task.TestLogPath = testLogPath ?? task.TestLogPath;
        task.FailureClassification = failureClassification;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        if (task.Status is TaskStatus.Running or TaskStatus.Paused or TaskStatus.Failed)
            await SaveCheckpointAsync(task, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    public async Task<bool> UpdateArtifactsAsync(
        Guid taskId, IReadOnlyList<TaskArtifactSnapshot> artifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Count > 500)
            throw new ArgumentException("At most 500 artifact references may be checkpointed.", nameof(artifacts));
        await using var fileLock = await _store.AcquireExclusiveLockAsync(cancellationToken);
        var latest = await _store.LoadUnlockedAsync(cancellationToken);
        var task = latest.FirstOrDefault(item => item.Id == taskId);
        if (task is null) return false;
        task.Artifacts = artifacts.ToList();
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
        if (task.Status is TaskStatus.Running or TaskStatus.Paused or TaskStatus.Failed)
            await SaveCheckpointAsync(task, cancellationToken);
        ReplaceTasks(latest);
        return true;
    }

    private Task SaveCheckpointAsync(DevelopmentTask task, CancellationToken cancellationToken)
    {
        if (_checkpointStore is null) return Task.CompletedTask;
        return _checkpointStore.SaveAsync(new TaskCheckpoint
        {
            TaskId = task.Id,
            Objective = string.IsNullOrWhiteSpace(task.Description) ? task.Title : task.Title + Environment.NewLine + task.Description,
            CompletedSteps = task.CompletedSteps.ToArray(),
            NextAction = task.CurrentStep,
            Artifacts = task.Artifacts.ToArray(),
            ProviderRoute = task.ProviderRoute,
            ModelAlias = task.ModelAlias,
            GitCommitSha = task.GitCommitSha,
            GitWorkingTreeStatus = task.GitWorkingTreeStatus,
            BuildLogPath = task.BuildLogPath,
            TestLogPath = task.TestLogPath,
            RetryCount = task.AttemptCount,
            FailureClassification = task.FailureClassification,
            IdempotencyKey = task.IdempotencyKey,
            CreatedAtUtc = task.CreatedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    public DevelopmentTask? Find(Guid taskId) =>
        _tasks.FirstOrDefault(task => task.Id == taskId);

    public DevelopmentTask? GetActiveTask() =>
        _tasks.FirstOrDefault(task =>
            task.Status == TaskStatus.Running ||
            task.Status == TaskStatus.Paused);

    private void ReplaceTasks(List<DevelopmentTask> latest)
    {
        // Keep references already handed to the development loop/UI alive while
        // refreshing their fields from the cross-process store. Replacing every
        // object made callers observe stale Pending/zero-attempt task instances.
        var existing = _tasks.ToDictionary(task => task.Id);
        var synchronized = new List<DevelopmentTask>(latest.Count);

        foreach (var saved in latest)
        {
            if (existing.TryGetValue(saved.Id, out var current))
            {
                current.Title = saved.Title;
                current.Description = saved.Description;
                current.Status = saved.Status;
                current.UpdatedAtUtc = saved.UpdatedAtUtc;
                current.AttemptCount = saved.AttemptCount;
                current.IsApproved = saved.IsApproved;
                current.ApprovedAtUtc = saved.ApprovedAtUtc;
                current.ApprovalConsumedAtUtc = saved.ApprovalConsumedAtUtc;
                current.CurrentStep = saved.CurrentStep;
                current.CompletedSteps = saved.CompletedSteps;
                current.ProviderRoute = saved.ProviderRoute;
                current.ModelAlias = saved.ModelAlias;
                current.GitCommitSha = saved.GitCommitSha;
                current.GitWorkingTreeStatus = saved.GitWorkingTreeStatus;
                current.BuildLogPath = saved.BuildLogPath;
                current.TestLogPath = saved.TestLogPath;
                current.FailureClassification = saved.FailureClassification;
                current.IdempotencyKey = saved.IdempotencyKey;
                current.Artifacts = saved.Artifacts;
                synchronized.Add(current);
            }
            else
            {
                synchronized.Add(saved);
            }
        }

        _tasks.Clear();
        _tasks.AddRange(synchronized);
    }
}
