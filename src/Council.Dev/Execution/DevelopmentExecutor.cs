using Council.Dev.Tasks;

namespace Council.Dev.Execution;

public interface IDevelopmentExecutor
{
    Task<DevelopmentExecutionResult> ExecuteAsync(
        DevelopmentTask task,
        CancellationToken cancellationToken = default);
}
