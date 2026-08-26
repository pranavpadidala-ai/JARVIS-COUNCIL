using System.Diagnostics;

namespace Council.Dev.Terminal;

public sealed class TerminalController : ITerminalExecutor
{
    private readonly CommandPolicy _policy;

    public TerminalController(CommandPolicy? policy = null)
    {
        _policy = policy ?? new CommandPolicy();
    }

    public async Task<TerminalResult> ExecuteAsync(
        TerminalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Command))
        {
            throw new ArgumentException(
                "Command cannot be empty.",
                nameof(command));
        }

        if (!Directory.Exists(command.WorkingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Working directory does not exist: {command.WorkingDirectory}");
        }

        if (!_policy.IsAllowed(command.Command))
        {
            throw new UnauthorizedAccessException(
                $"Command blocked by policy: {command.Command}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();

        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{command.Command}\"",
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource =
            new CancellationTokenSource(command.Timeout);

        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        var timedOut = false;
        var cancelled = false;

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut =
                timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested;

            cancelled =
                cancellationToken.IsCancellationRequested;

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process may already have exited.
            }

            await outputTask;
            await errorTask;

            stopwatch.Stop();

            if (cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        var standardOutput = await outputTask;
        var standardError = await errorTask;

        stopwatch.Stop();

        return new TerminalResult(
            process.HasExited ? process.ExitCode : -1,
            standardOutput,
            standardError,
            stopwatch.Elapsed,
            timedOut,
            cancelled);
    }
}
