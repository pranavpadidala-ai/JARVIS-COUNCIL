namespace Council.Dev.Git;

public interface IGitCheckpointManager
{
    Task<GitCheckpointResult> CreateCheckpointAsync(
        CancellationToken cancellationToken = default);

    Task<GitCheckpointResult> CommitAsync(
        string message,
        CancellationToken cancellationToken = default);

    Task<GitCheckpointResult> RollbackAsync(
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken = default);
}
