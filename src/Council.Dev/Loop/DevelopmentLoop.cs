using Council.Dev.Runner;
using Council.Dev.Tasks;
using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Loop;

public sealed class DevelopmentLoop
{
    private readonly TaskManager _taskManager;
    private readonly TaskRunner _taskRunner;

    public DevelopmentLoop(
        TaskManager taskManager,
        TaskRunner taskRunner)
    {
        _taskManager = taskManager;
        _taskRunner = taskRunner;
    }

    public async Task RunAsync(
        DevelopmentTask task,
        Func<DevelopmentTask, CancellationToken, Task<DevelopmentCycleResult>> executor,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(executor);

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "Maximum attempts must be greater than zero.");
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _taskManager.IncrementAttemptAsync(
                task.Id,
                CancellationToken.None);

            await _taskManager.UpdateStepAsync(
                task.Id,
                $"Development attempt {attempt} of {maxAttempts}",
                CancellationToken.None);

            DevelopmentCycleResult? cycleResult = null;

            try
            {
                await _taskRunner.RunAsync(
                    task,
                    async (currentTask, token) =>
                    {
                        cycleResult = await executor(
                            currentTask,
                            token);
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (attempt == maxAttempts)
                {
                    throw;
                }

                continue;
            }

            if (cycleResult is null)
            {
                throw new InvalidOperationException(
                    "Development executor did not return a result.");
            }

            if (cycleResult.Success)
            {
                await _taskManager.UpdateStepAsync(
                    task.Id,
                    cycleResult.Message,
                    CancellationToken.None);

                return;
            }

            await _taskManager.UpdateStepAsync(
                task.Id,
                cycleResult.Message,
                CancellationToken.None);

            if (attempt == maxAttempts)
            {
                await _taskManager.UpdateStatusAsync(
                    task.Id,
                    CouncilTaskStatus.Failed,
                    CancellationToken.None);

                throw new InvalidOperationException(
                    $"Development failed after {maxAttempts} attempts: " +
                    cycleResult.Message);
            }
        }
    }
}
