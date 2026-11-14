using Council.Dev.Terminal;

namespace Council.Dev.Validation;

public sealed class BuildTestExecutor
{
    private readonly ITerminalExecutor _terminalExecutor;
    private readonly string _workingDirectory;

    public BuildTestExecutor(
        ITerminalExecutor terminalExecutor,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(terminalExecutor);

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException(
                "Working directory is required.",
                nameof(workingDirectory));
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Working directory does not exist: {workingDirectory}");
        }

        _terminalExecutor = terminalExecutor;
        _workingDirectory = workingDirectory;
    }

    public async Task<ValidationResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var buildResult = await _terminalExecutor.ExecuteAsync(
            new TerminalCommand(
                "dotnet build",
                _workingDirectory,
                TimeSpan.FromMinutes(5)),
            cancellationToken);

        if (!buildResult.Success)
        {
            return ValidationResult.Failed(
                "Build failed.",
                buildResult);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var testResult = await _terminalExecutor.ExecuteAsync(
            new TerminalCommand(
                "dotnet test",
                _workingDirectory,
                TimeSpan.FromMinutes(5)),
            cancellationToken);

        if (!testResult.Success)
        {
            return ValidationResult.Failed(
                "Tests failed.",
                testResult);
        }

        TerminalResult? pythonTestResult = null;
        var pythonTestsDirectory = Path.Combine(_workingDirectory, "tests", "python");
        if (Directory.Exists(pythonTestsDirectory))
        {
            var syntaxResult = await _terminalExecutor.ExecuteAsync(
                new TerminalCommand(
                    "py -3.11 -m compileall -q python tests/python scripts",
                    _workingDirectory,
                    TimeSpan.FromMinutes(2)),
                cancellationToken);

            if (!syntaxResult.Success)
            {
                return ValidationResult.FailedPythonValidation(
                    "Python syntax validation failed.",
                    syntaxResult);
            }

            pythonTestResult = await _terminalExecutor.ExecuteAsync(
                new TerminalCommand(
                    "py -3.11 -m unittest discover -s tests/python -v",
                    _workingDirectory,
                    TimeSpan.FromMinutes(3)),
                cancellationToken);

            if (!pythonTestResult.Success)
            {
                return ValidationResult.FailedPythonValidation(
                    "Python unit tests failed.",
                    pythonTestResult);
            }
        }

        return ValidationResult.Passed(
            pythonTestResult is null
                ? "Build and .NET tests passed."
                : "Build, .NET tests, Python syntax checks, and Python unit tests passed.",
            buildResult,
            testResult,
            pythonTestResult);
    }
}
