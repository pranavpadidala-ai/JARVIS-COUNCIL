using Council.Dev.Terminal;

namespace Council.Dev.Validation;

public sealed class BuildTestExecutor
{
    private readonly ITerminalExecutor _terminalExecutor;
    private readonly string _workingDirectory;
    private readonly ValidationLogStore _logStore;

    public BuildTestExecutor(
        ITerminalExecutor terminalExecutor,
        string workingDirectory,
        ValidationLogStore? logStore = null)
    {
        ArgumentNullException.ThrowIfNull(terminalExecutor);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException("Working directory is required.", nameof(workingDirectory));
        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Working directory does not exist: {workingDirectory}");

        _terminalExecutor = terminalExecutor;
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _logStore = logStore ?? new ValidationLogStore();
    }

    public async Task<ValidationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];

        var buildResult = await _terminalExecutor.ExecuteAsync(
            new TerminalCommand("dotnet build", _workingDirectory, TimeSpan.FromMinutes(5)),
            cancellationToken);
        var buildLog = await _logStore.SaveAsync(runId, "build", buildResult, cancellationToken);

        if (!buildResult.Success)
            return ValidationResult.Failed("Build failed.", buildResult) with { BuildLogPath = buildLog };

        cancellationToken.ThrowIfCancellationRequested();
        var testResult = await _terminalExecutor.ExecuteAsync(
            new TerminalCommand("dotnet test", _workingDirectory, TimeSpan.FromMinutes(5)),
            cancellationToken);
        var testLog = await _logStore.SaveAsync(runId, "test", testResult, cancellationToken);

        if (!testResult.Success)
            return ValidationResult.Failed("Tests failed.", testResult) with
            {
                BuildLogPath = buildLog,
                TestLogPath = testLog
            };

        return ValidationResult.Passed("Build and tests passed.", buildResult, testResult) with
        {
            BuildLogPath = buildLog,
            TestLogPath = testLog
        };
    }
}