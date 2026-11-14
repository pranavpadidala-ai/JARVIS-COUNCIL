using Council.Dev.Terminal;

namespace Council.Dev.Validation;

public sealed record ValidationResult(
    bool Success,
    string Message,
    TerminalResult? BuildResult,
    TerminalResult? TestResult,
    TerminalResult? PythonValidationResult = null)
{
    public static ValidationResult Passed(
        string message,
        TerminalResult buildResult,
        TerminalResult testResult,
        TerminalResult? pythonValidationResult = null)
        => new(
            true,
            message,
            buildResult,
            testResult,
            pythonValidationResult);

    public static ValidationResult Failed(
        string message,
        TerminalResult failedResult)
        => new(
            false,
            message,
            failedResult,
            null);

    public static ValidationResult FailedPythonValidation(
        string message,
        TerminalResult pythonValidationResult)
        => new(
            false,
            message,
            null,
            null,
            pythonValidationResult);
}
