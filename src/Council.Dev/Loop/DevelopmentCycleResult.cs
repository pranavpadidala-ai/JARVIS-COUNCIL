namespace Council.Dev.Loop;

public sealed record DevelopmentCycleResult(
    bool Success,
    string Message)
{
    public static DevelopmentCycleResult Passed(
        string message = "Development cycle completed successfully.")
        => new(true, message);

    public static DevelopmentCycleResult Failed(
        string message)
        => new(false, message);
}
