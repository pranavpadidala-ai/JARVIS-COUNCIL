using Council.Dev.Actions;

namespace Council.Dev.Workspace;

public interface IWorkspaceExecutor
{
    Task<WorkspaceActionResult> ExecuteAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken = default);
}
