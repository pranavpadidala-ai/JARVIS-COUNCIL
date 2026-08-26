namespace Council.Dev.Actions;

public sealed record DevelopmentActionResult(
    bool Success,
    string Message,
    string? Content = null)
{
    public static DevelopmentActionResult Completed(
        string message,
        string? content = null)
        => new(true, message, content);

    public static DevelopmentActionResult Failed(
        string message,
        string? content = null)
        => new(false, message, content);
}
