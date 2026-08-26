using Council.Dev.Tasks;
using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Tests;

public class TaskPersistenceTests
{
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
}
