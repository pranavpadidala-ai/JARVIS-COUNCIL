using Council.Dev.Tasks;
using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Runner;

public sealed class TaskRunner
{
    private readonly TaskManager _taskManager;

    public TaskRunner(TaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public async Task RunAsync(
        DevelopmentTask task,
        Func<DevelopmentTask, CancellationToken, Task> executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(executor);

        await _taskManager.UpdateStatusAsync(
            task.Id,
            CouncilTaskStatus.Running,
            CancellationToken.None);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await executor(task, cancellationToken);

            await _taskManager.UpdateStatusAsync(
                task.Id,
                CouncilTaskStatus.Completed,
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await _taskManager.UpdateStatusAsync(
                task.Id,
                CouncilTaskStatus.Paused,
                CancellationToken.None);

            throw;
        }
        catch
        {
            await _taskManager.UpdateStatusAsync(
                task.Id,
                CouncilTaskStatus.Failed,
                CancellationToken.None);

            throw;
        }
    }
}
