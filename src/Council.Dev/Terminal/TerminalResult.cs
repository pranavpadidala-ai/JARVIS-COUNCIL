namespace Council.Dev.Terminal;

public sealed record TerminalResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled)
{
    public bool Success =>
        ExitCode == 0 &&
        !TimedOut &&
        !Cancelled;

    public static TerminalResult SuccessResult()
    {
        return new TerminalResult(
            0,
            "Success",
            string.Empty,
            TimeSpan.Zero,
            false,
            false);
    }

    public static TerminalResult FailedResult()
    {
        return new TerminalResult(
            1,
            string.Empty,
            "Failure",
            TimeSpan.Zero,
            false,
            false);
    }
}
