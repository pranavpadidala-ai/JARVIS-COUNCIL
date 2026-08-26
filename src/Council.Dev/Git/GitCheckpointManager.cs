using System.Text.RegularExpressions;
using Council.Dev.Terminal;

namespace Council.Dev.Git;

public sealed class GitCheckpointManager : IGitCheckpointManager
{
    private static readonly Regex CommitShaRegex =
        new(
            "^[0-9a-fA-F]{40}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ITerminalExecutor _terminalExecutor;
    private readonly string _workingDirectory;

    public GitCheckpointManager(
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
        _workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public async Task<GitCheckpointResult> CreateCheckpointAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = await ExecuteGitAsync(
            "git status --porcelain",
            cancellationToken);

        if (!status.Success)
        {
            return GitCheckpointResult.Failed(
                "Unable to inspect Git working tree. " +
                GetOutput(status));
        }

        if (!string.IsNullOrWhiteSpace(status.StandardOutput))
        {
            return GitCheckpointResult.Failed(
                "Git working tree must be clean before creating " +
                "an autonomous checkpoint. Existing changes detected:" +
                Environment.NewLine +
                status.StandardOutput.Trim());
        }

        cancellationToken.ThrowIfCancellationRequested();

        var head = await ExecuteGitAsync(
            "git rev-parse HEAD",
            cancellationToken);

        if (!head.Success)
        {
            return GitCheckpointResult.Failed(
                "Unable to determine current Git commit. " +
                GetOutput(head));
        }

        var sha = head.StandardOutput.Trim();

        if (!IsValidCommitSha(sha))
        {
            return GitCheckpointResult.Failed(
                "Git returned an invalid HEAD commit SHA.");
        }

        return GitCheckpointResult.Completed(
            $"Git checkpoint created at {sha}.",
            new GitCheckpoint(sha));
    }

    public async Task<GitCheckpointResult> CommitAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return GitCheckpointResult.Failed(
                "Commit message is required.");
        }

        var commitMessage = SanitizeCommitMessage(message);

        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            return GitCheckpointResult.Failed(
                "Commit message is empty after sanitization.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var status = await ExecuteGitAsync(
            "git status --porcelain",
            cancellationToken);

        if (!status.Success)
        {
            return GitCheckpointResult.Failed(
                "Unable to inspect Git working tree before commit. " +
                GetOutput(status));
        }

        if (string.IsNullOrWhiteSpace(status.StandardOutput))
        {
            return GitCheckpointResult.Completed(
                "No Git changes to commit.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var add = await ExecuteGitAsync(
            "git add -A",
            cancellationToken);

        if (!add.Success)
        {
            return GitCheckpointResult.Failed(
                "Git staging failed. " +
                GetOutput(add));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Use PowerShell single-quoted string syntax.
        // Single quotes prevent variable expansion and command
        // substitution such as $(...).
        var safeMessage =
            commitMessage.Replace("'", "''");

        var commit = await ExecuteGitAsync(
            $"git commit -m '{safeMessage}'",
            cancellationToken);

        if (!commit.Success)
        {
            return GitCheckpointResult.Failed(
                "Git commit failed. " +
                GetOutput(commit));
        }

        return GitCheckpointResult.Completed(
            $"Git commit created successfully: {commitMessage}");
    }

    public async Task<GitCheckpointResult> RollbackAsync(
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!IsValidCommitSha(checkpoint.CommitSha))
        {
            return GitCheckpointResult.Failed(
                "Checkpoint commit SHA is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var reset = await ExecuteGitAsync(
            $"git reset --hard {checkpoint.CommitSha}",
            cancellationToken);

        if (!reset.Success)
        {
            return GitCheckpointResult.Failed(
                "Git rollback failed during reset. " +
                GetOutput(reset));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var clean = await ExecuteGitAsync(
            "git clean -fd",
            cancellationToken);

        if (!clean.Success)
        {
            return GitCheckpointResult.Failed(
                "Git rollback reset succeeded, but cleaning " +
                "autonomously-created files failed. " +
                GetOutput(clean));
        }

        return GitCheckpointResult.Completed(
            $"Repository rolled back to checkpoint {checkpoint.CommitSha}.");
    }

    private async Task<TerminalResult> ExecuteGitAsync(
        string command,
        CancellationToken cancellationToken)
    {
        return await _terminalExecutor.ExecuteAsync(
            new TerminalCommand(
                command,
                _workingDirectory,
                TimeSpan.FromMinutes(5)),
            cancellationToken);
    }

    private static bool IsValidCommitSha(string sha)
    {
        return !string.IsNullOrWhiteSpace(sha) &&
               CommitShaRegex.IsMatch(sha);
    }

    private static string GetOutput(
        TerminalResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError.Trim();
        }

        return result.StandardOutput.Trim();
    }

    private static string SanitizeCommitMessage(
        string message)
    {
        return message
            .Replace("\0", "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }
}
