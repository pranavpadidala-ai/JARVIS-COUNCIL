using Council.Dev.Terminal;

namespace Council.Dev.Validation;

public sealed record ValidationResult(
    bool Success,
    string Message,
    TerminalResult? BuildResult,
    TerminalResult? TestResult)
{
    public string? BuildLogPath { get; init; }
    public string? TestLogPath { get; init; }

    public static ValidationResult Passed(
        string message,
        TerminalResult buildResult,
        TerminalResult testResult)
        => new(true, message, buildResult, testResult);

    public static ValidationResult Failed(
        string message,
        TerminalResult failedResult)
        => new(false, message, failedResult, null);
}