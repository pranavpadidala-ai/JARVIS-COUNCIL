namespace Council.Dev.Execution;

public sealed record DevelopmentExecutionResult(
    bool Success,
    string Message)
{
    public static DevelopmentExecutionResult Completed(
        string message = "Development execution completed.")
        => new(true, message);

    public static DevelopmentExecutionResult Failed(
        string message)
        => new(false, message);
}
