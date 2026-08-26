namespace Council.Dev.Git;

public sealed record GitCheckpointResult(
    bool Success,
    string Message,
    GitCheckpoint? Checkpoint = null)
{
    public static GitCheckpointResult Completed(
        string message,
        GitCheckpoint? checkpoint = null)
        => new(true, message, checkpoint);

    public static GitCheckpointResult Failed(
        string message)
        => new(false, message);
}
