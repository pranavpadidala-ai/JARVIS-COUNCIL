using Council.Dev.Git;
using Council.Dev.Loop;
using Council.Dev.Tasks;

namespace Council.Dev.Execution;

public sealed class DevelopmentOrchestrator
{
    private readonly DevelopmentLoop _developmentLoop;
    private readonly IDevelopmentExecutor _executor;
    private readonly IGitCheckpointManager _gitCheckpointManager;

    public DevelopmentOrchestrator(
        DevelopmentLoop developmentLoop,
        IDevelopmentExecutor executor,
        IGitCheckpointManager gitCheckpointManager)
    {
        ArgumentNullException.ThrowIfNull(developmentLoop);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(gitCheckpointManager);

        _developmentLoop = developmentLoop;
        _executor = executor;
        _gitCheckpointManager = gitCheckpointManager;
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
                    var checkpoint =
                        await _gitCheckpointManager
                            .CreateCheckpointAsync(token);

                    if (!checkpoint.Success ||
                        checkpoint.Checkpoint is null)
                    {
                        finalResult =
                            DevelopmentCycleResult.Failed(
                                checkpoint.Message);

                        return finalResult;
                    }

                    var executionResult =
                        await _executor.ExecuteAsync(
                            currentTask,
                            token);

                    if (executionResult.Success)
                    {
                        var commit =
                            await _gitCheckpointManager.CommitAsync(
                                $"Autonomous development: {currentTask.Title}",
                                token);

                        if (!commit.Success)
                        {
                            await TryRollbackAsync(
                                checkpoint.Checkpoint,
                                token);

                            finalResult =
                                DevelopmentCycleResult.Failed(
                                    "Development succeeded, but " +
                                    "Git commit failed: " +
                                    commit.Message);

                            return finalResult;
                        }

                        finalResult =
                            DevelopmentCycleResult.Passed(
                                executionResult.Message +
                                " " +
                                commit.Message);

                        return finalResult;
                    }

                    var rollback =
                        await _gitCheckpointManager.RollbackAsync(
                            checkpoint.Checkpoint,
                            token);

                    var failureMessage =
                        executionResult.Message +
                        Environment.NewLine +
                        "Autonomous attempt rolled back: " +
                        rollback.Message;

                    finalResult =
                        DevelopmentCycleResult.Failed(
                            failureMessage);

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

    private async Task TryRollbackAsync(
        GitCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gitCheckpointManager.RollbackAsync(
                checkpoint,
                cancellationToken);
        }
        catch
        {
            // Preserve the original commit failure.
            // Rollback failure is intentionally not allowed
            // to hide the primary failure.
        }
    }
}
