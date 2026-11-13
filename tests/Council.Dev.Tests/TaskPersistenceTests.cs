using Council.Dev.Tasks;
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
        var testFile = Path.Combine(
            Path.GetTempPath(),
            $"jarvis-task-test-{Guid.NewGuid():N}.json");

        try
        {
            var store = new TaskStore(testFile);

            var manager = new TaskManager(store);

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
                new TaskStore(testFile));

            await secondManager.InitializeAsync();

            var loadedTask = secondManager.Find(createdTask.Id);

            Assert.NotNull(loadedTask);

            Assert.Equal(
                "Persistence test",
                loadedTask!.Title);

            Assert.Equal(
                CouncilTaskStatus.Running,
                loadedTask.Status);

            Assert.Equal(
                "Testing persistence",
                loadedTask.CurrentStep);
        }
        finally
        {
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
        }
    }

    [Fact]
    public async Task ConcurrentManagers_DoNotLoseTaskQueueUpdates()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "tasks.json");
        try
        {
            var managers = Enumerable.Range(0, 4)
                .Select(_ => new TaskManager(new TaskStore(file)))
                .ToArray();
            await Task.WhenAll(managers.Select(manager => manager.InitializeAsync()));

            var writes = Enumerable.Range(0, 40)
                .Select(index => managers[index % managers.Length]
                    .CreateAsync("Concurrent task " + index, "Cross-process store test."));
            await Task.WhenAll(writes);

            var reloaded = new TaskManager(new TaskStore(file));
            await reloaded.InitializeAsync();
            Assert.Equal(40, reloaded.Tasks.Count);
            Assert.Equal(40, reloaded.Tasks.Select(task => task.Id).Distinct().Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BackgroundApproval_IsConsumedOnce_AndInterruptedWorkNeedsFreshApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "tasks.json");
        try
        {
            var manager = new TaskManager(new TaskStore(file));
            await manager.InitializeAsync();
            var task = await manager.CreateAsync("Approved work", "Must be one-shot.");
            Assert.True(await manager.ApproveAsync(task.Id));

            var claimed = await manager.ClaimNextApprovedTaskAsync();
            Assert.NotNull(claimed);
            Assert.Equal(task.Id, claimed!.Id);
            Assert.NotNull(claimed.ApprovalConsumedAtUtc);
            Assert.False(await manager.RevokeApprovalAsync(task.Id));
            Assert.False(await manager.ApproveAsync(task.Id));
            Assert.Null(await manager.ClaimNextApprovedTaskAsync());

            Assert.Equal(1, await manager.RecoverInterruptedTasksAsync());
            var reloaded = new TaskManager(new TaskStore(file));
            await reloaded.InitializeAsync();
            var paused = reloaded.Find(task.Id);
            Assert.NotNull(paused);
            Assert.Equal(CouncilTaskStatus.Paused, paused!.Status);
            Assert.False(paused.IsApproved);
            Assert.Null(await reloaded.ClaimNextApprovedTaskAsync());

            Assert.True(await reloaded.ApproveAsync(task.Id));
            var reapproved = await reloaded.ClaimNextApprovedTaskAsync();
            Assert.NotNull(reapproved);
            Assert.Equal(task.Id, reapproved!.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RevokedApproval_CannotBeClaimedByBackgroundAgent()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "tasks.json");
        try
        {
            var manager = new TaskManager(new TaskStore(file));
            await manager.InitializeAsync();
            var task = await manager.CreateAsync("Revoked work", "Should remain pending.");
            Assert.True(await manager.ApproveAsync(task.Id));
            Assert.True(await manager.RevokeApprovalAsync(task.Id));

            Assert.Null(await manager.ClaimNextApprovedTaskAsync());
            var reloaded = new TaskManager(new TaskStore(file));
            await reloaded.InitializeAsync();
            var saved = reloaded.Find(task.Id);
            Assert.NotNull(saved);
            Assert.False(saved!.IsApproved);
            Assert.Equal(CouncilTaskStatus.Pending, saved.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
