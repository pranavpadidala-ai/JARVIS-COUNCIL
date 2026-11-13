using Council.Dev.Tasks;

namespace Council.Dev.Recovery;

public sealed class TaskRecoveryService
{
    private readonly TaskCheckpointStore _checkpoints;
    private readonly TaskManager _tasks;

    public TaskRecoveryService(TaskCheckpointStore checkpoints, TaskManager tasks)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(tasks);
        _checkpoints = checkpoints;
        _tasks = tasks;
    }

    public Task SaveAsync(TaskCheckpoint checkpoint, CancellationToken cancellationToken = default)
        => _checkpoints.SaveAsync(checkpoint, cancellationToken);

    public async Task<TaskCheckpoint?> RecoverAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var checkpoint = await _checkpoints.LoadAsync(taskId, cancellationToken);
        if (checkpoint is null) return null;

        var task = _tasks.Find(taskId);
        if (task is not null)
        {
            await _tasks.UpdateStepAsync(taskId,
                $"Recovered checkpoint from {checkpoint.CreatedAtUtc:O}. Next: {checkpoint.NextAction}",
                cancellationToken);
            await _tasks.UpdateStatusAsync(taskId, TaskStatus.Paused, cancellationToken);
        }
        return checkpoint;
    }

    public Task<IReadOnlyList<TaskCheckpoint>> ListAsync(CancellationToken cancellationToken = default)
        => _checkpoints.ListAsync(cancellationToken);
}