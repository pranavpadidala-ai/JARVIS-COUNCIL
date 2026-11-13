namespace Council.Dev.Tasks;

public sealed class TaskManager
{
    private readonly TaskStore _store;
    private readonly List<DevelopmentTask> _tasks = [];

    public TaskManager(TaskStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
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
        var recovered = 0;

        foreach (var task in latest)
        {
            var wasRunning = task.Status == TaskStatus.Running;
            var wasClaimedButNotStarted =
                task.Status == TaskStatus.Pending && task.ApprovalConsumedAtUtc.HasValue;
            if (!wasRunning && !wasClaimedButNotStarted)
                continue;

            task.Status = TaskStatus.Paused;
            task.IsApproved = false;
            task.UpdatedAtUtc = DateTime.UtcNow;
            task.CurrentStep = "Interrupted by host shutdown; fresh user approval is required to resume.";
            recovered++;
        }

        if (recovered > 0)
            await _store.SaveUnlockedAsync(latest, cancellationToken);
        ReplaceTasks(latest);
        return recovered;
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
        task.UpdatedAtUtc = DateTime.UtcNow;
        await _store.SaveUnlockedAsync(latest, cancellationToken);
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
        ReplaceTasks(latest);
        return true;
    }

    public DevelopmentTask? Find(Guid taskId) =>
        _tasks.FirstOrDefault(task => task.Id == taskId);

    public DevelopmentTask? GetActiveTask() =>
        _tasks.FirstOrDefault(task =>
            task.Status == TaskStatus.Running ||
            task.Status == TaskStatus.Paused);

    private void ReplaceTasks(List<DevelopmentTask> latest)
    {
        _tasks.Clear();
        _tasks.AddRange(latest);
    }
}
