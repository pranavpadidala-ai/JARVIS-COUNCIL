namespace Council.Dev.Workspace;

public sealed record WorkspaceActionResult(
    bool Success,
    string Message,
    string? Content = null)
{
    public static WorkspaceActionResult Completed(
        string message,
        string? content = null)
        => new(true, message, content);

    public static WorkspaceActionResult Failed(
        string message)
        => new(false, message);
}
