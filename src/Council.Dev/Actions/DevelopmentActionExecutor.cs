using Council.Dev.Terminal;
using Council.Dev.Workspace;

namespace Council.Dev.Actions;

public sealed class DevelopmentActionExecutor
{
    private readonly IWorkspaceExecutor _workspaceExecutor;
    private readonly ITerminalExecutor _terminalExecutor;
    private readonly string _workingDirectory;

    public DevelopmentActionExecutor(
        IWorkspaceExecutor workspaceExecutor,
        ITerminalExecutor terminalExecutor,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(workspaceExecutor);
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

        _workspaceExecutor = workspaceExecutor;
        _terminalExecutor = terminalExecutor;
        _workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public async Task<DevelopmentActionResult> ExecuteAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();

        switch (action.Type)
        {
            case DevelopmentActionType.ReadFile:
            case DevelopmentActionType.SearchCode:
            case DevelopmentActionType.CreateFile:
            case DevelopmentActionType.ModifyFile:
            case DevelopmentActionType.DeleteFile:
                var workspaceResult =
                    await _workspaceExecutor.ExecuteAsync(
                        action,
                        cancellationToken);

                return new DevelopmentActionResult(
                    workspaceResult.Success,
                    workspaceResult.Message,
                    workspaceResult.Content);

            case DevelopmentActionType.RunBuild:
                return await RunTerminalActionAsync(
                    "dotnet build",
                    cancellationToken);

            case DevelopmentActionType.RunTests:
                return await RunTerminalActionAsync(
                    "dotnet test",
                    cancellationToken);

            case DevelopmentActionType.GitDiff:
                return await RunTerminalActionAsync(
                    "git diff",
                    cancellationToken);

            default:
                return DevelopmentActionResult.Failed(
                    $"Unsupported action type: {action.Type}.");
        }
    }

    private async Task<DevelopmentActionResult> RunTerminalActionAsync(
        string command,
        CancellationToken cancellationToken)
    {
        var result =
            await _terminalExecutor.ExecuteAsync(
                new TerminalCommand(
                    command,
                    _workingDirectory,
                    TimeSpan.FromMinutes(5)),
                cancellationToken);

        var message =
            result.Success
                ? $"Command completed successfully: {command}"
                : $"Command failed: {command}";

        var content =
            string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError
                : result.StandardOutput;

        return new DevelopmentActionResult(
            result.Success,
            message,
            content);
    }
}
