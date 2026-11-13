using Council.Dev.Tasks;
using Council.Dev.Recovery;
using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Tests;

public class TaskPersistenceTests
{
    [Fact]
    public async Task FailedTaskPersistenceDoesNotLeaveAnUnsavedTaskInMemory()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var directoryUsedAsFile = Path.Combine(root, "task-store.json");
        Directory.CreateDirectory(directoryUsedAsFile);
        try
        {
            var manager = new TaskManager(new TaskStore(directoryUsedAsFile));
            await manager.InitializeAsync();

            var exception = await Record.ExceptionAsync(() =>
                manager.CreateAsync("Should not remain", "The persistence target is invalid."));

            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.Empty(manager.Tasks);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Task_survives_save_and_reload()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"jarvis-task-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        var testFile = Path.Combine(testRoot, "tasks.json");
        var checkpointRoot = Path.Combine(testRoot, "checkpoints");

        try
        {
            var store = new TaskStore(testFile);

            var manager = new TaskManager(store, new TaskCheckpointStore(checkpointRoot));

            await manager.InitializeAsync();

            var createdTask = await manager.CreateAsync(
                "Persistence test",
                "Verify that JARVIS development tasks survive restart.");

            await manager.UpdateStatusAsync(
                createdTask.Id,
                CouncilTaskStatus.Running);

            await manager.UpdateStepAsync(
                createdTask.Id,
                "Testing persistence");

            var secondManager = new TaskManager(
                new TaskStore(testFile), new TaskCheckpointStore(checkpointRoot));

            await secondManager.InitializeAsync();

            var loadedTask = secondManager.Find(createdTask.Id);

            Assert.NotNull(loadedTask);

            Assert.Equal(
                "Persistence test",
                loadedTask!.Title);

            Assert.Equal(
                CouncilTaskStatus.Paused,
                loadedTask.Status);

            Assert.Equal(
                "Testing persistence",
                loadedTask.CurrentStep);
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }
}
