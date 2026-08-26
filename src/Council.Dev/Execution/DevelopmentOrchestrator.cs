using Council.Dev.Loop;
using Council.Dev.Tasks;

namespace Council.Dev.Execution;

public sealed class DevelopmentOrchestrator
{
    private readonly DevelopmentLoop _developmentLoop;
    private readonly IDevelopmentExecutor _executor;

    public DevelopmentOrchestrator(
        DevelopmentLoop developmentLoop,
        IDevelopmentExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(developmentLoop);
        ArgumentNullException.ThrowIfNull(executor);

        _developmentLoop = developmentLoop;
        _executor = executor;
    }

    public async Task<DevelopmentExecutionResult> ExecuteAsync(
        DevelopmentTask task,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        cancellationToken.ThrowIfCancellationRequested();

        DevelopmentCycleResult? finalResult = null;

        try
        {
            await _developmentLoop.RunAsync(
                task,
                async (currentTask, token) =>
                {
                    var executionResult =
                        await _executor.ExecuteAsync(
                            currentTask,
                            token);

                    finalResult = executionResult.Success
                        ? DevelopmentCycleResult.Passed(
                            executionResult.Message)
                        : DevelopmentCycleResult.Failed(
                            executionResult.Message);

                    return finalResult;
                },
                maxAttempts,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DevelopmentExecutionResult.Failed(
                ex.Message);
        }

        return finalResult is { Success: true }
            ? DevelopmentExecutionResult.Completed(
                finalResult.Message)
            : DevelopmentExecutionResult.Failed(
                finalResult?.Message ??
                "Development execution failed.");
    }
}
