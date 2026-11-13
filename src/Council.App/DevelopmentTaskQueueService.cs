using Council.Dev.Tasks;

namespace Council.App;

/// <summary>
/// Desktop-facing view of the same durable development-task store used by
/// JARVIS tools and the optional background agent. It never runs tasks.
/// </summary>
public sealed class DevelopmentTaskQueueService
{
    private readonly TaskManager _manager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public DevelopmentTaskQueueService(string? taskStorePath = null)
    {
        var configuredTaskStore = taskStorePath ?? Environment.GetEnvironmentVariable("JARVIS_TASK_STORE_PATH");
        _manager = new TaskManager(new TaskStore(configuredTaskStore));
    }

    public async Task<IReadOnlyList<DevelopmentTask>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await _manager.RefreshAsync(cancellationToken);
            return _manager.Tasks
                .OrderByDescending(task => task.UpdatedAtUtc)
                .Take(100)
                .Select(Clone)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DevelopmentTask> CreateAsync(
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var created = await _manager.CreateAsync(title, description, cancellationToken);
            return Clone(created);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ApproveAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return await _manager.ApproveAsync(taskId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RevokeApprovalAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return await _manager.RevokeApprovalAsync(taskId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _manager.InitializeAsync(cancellationToken);
        _initialized = true;
    }

    private static DevelopmentTask Clone(DevelopmentTask task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Status = task.Status,
        CreatedAtUtc = task.CreatedAtUtc,
        UpdatedAtUtc = task.UpdatedAtUtc,
        AttemptCount = task.AttemptCount,
        IsApproved = task.IsApproved,
        ApprovedAtUtc = task.ApprovedAtUtc,
        ApprovalConsumedAtUtc = task.ApprovalConsumedAtUtc,
        CurrentStep = task.CurrentStep
    };
}
