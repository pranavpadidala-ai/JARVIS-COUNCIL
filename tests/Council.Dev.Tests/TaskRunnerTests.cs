using Council.Dev.Runner;
using Council.Dev.Tasks;
using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Tests;

public class TaskRunnerTests
{
    [Fact]
    public async Task Successful_task_becomes_completed()
    {
        var testFile = CreateTempFile();

        try
        {
            var store = new TaskStore(testFile);
            var manager = new TaskManager(store);

            await manager.InitializeAsync();

            var task = await manager.CreateAsync(
                "Successful task",
                "Test successful execution.");

            var runner = new TaskRunner(manager);

            await runner.RunAsync(
                task,
                async (currentTask, cancellationToken) =>
                {
                    await manager.UpdateStepAsync(
                        currentTask.Id,
                        "Executing");

                    await Task.Delay(10, cancellationToken);
                });

            var result = manager.Find(task.Id);

            Assert.NotNull(result);
            Assert.Equal(
                CouncilTaskStatus.Completed,
                result!.Status);
        }
        finally
        {
            DeleteTempFile(testFile);
        }
    }

    [Fact]
    public async Task Cancelled_task_becomes_paused()
    {
        var testFile = CreateTempFile();

        try
        {
            var store = new TaskStore(testFile);
            var manager = new TaskManager(store);

            await manager.InitializeAsync();

            var task = await manager.CreateAsync(
                "Cancelled task",
                "Test cancellation.");

            var runner = new TaskRunner(manager);

            using var cancellationSource =
                new CancellationTokenSource();

            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runner.RunAsync(
                    task,
                    async (_, cancellationToken) =>
                    {
                        await Task.Delay(
                            1000,
                            cancellationToken);
                    },
                    cancellationSource.Token));

            var result = manager.Find(task.Id);

            Assert.NotNull(result);
            Assert.Equal(
                CouncilTaskStatus.Paused,
                result!.Status);
        }
        finally
        {
            DeleteTempFile(testFile);
        }
    }

    [Fact]
    public async Task Failed_task_becomes_failed()
    {
        var testFile = CreateTempFile();

        try
        {
            var store = new TaskStore(testFile);
            var manager = new TaskManager(store);

            await manager.InitializeAsync();

            var task = await manager.CreateAsync(
                "Failed task",
                "Test failure handling.");

            var runner = new TaskRunner(manager);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => runner.RunAsync(
                    task,
                    (_, _) =>
                        throw new InvalidOperationException(
                            "Simulated failure")));

            var result = manager.Find(task.Id);

            Assert.NotNull(result);
            Assert.Equal(
                CouncilTaskStatus.Failed,
                result!.Status);
        }
        finally
        {
            DeleteTempFile(testFile);
        }
    }

    private static string CreateTempFile()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"jarvis-runner-test-{Guid.NewGuid():N}.json");
    }

    private static void DeleteTempFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
