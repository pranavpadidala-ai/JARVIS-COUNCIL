using Council.Dev.Terminal;

namespace Council.Dev.Validation;

public sealed record ValidationResult(
    bool Success,
    string Message,
    TerminalResult? BuildResult,
    TerminalResult? TestResult,
    TerminalResult? PythonTestResult = null)
{
    public static ValidationResult Passed(
        string message,
        TerminalResult buildResult,
        TerminalResult testResult,
        TerminalResult? pythonTestResult = null)
        => new(
            true,
            message,
            buildResult,
            testResult,
            pythonTestResult);

    public static ValidationResult Failed(
        string message,
        TerminalResult failedResult)
        => new(
            false,
            message,
            failedResult,
            null);

    public static ValidationResult FailedPythonTests(
        string message,
        TerminalResult pythonTestResult)
        => new(
            false,
            message,
            null,
            null,
            pythonTestResult);
}
