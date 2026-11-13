using Council.App;
using Council.Dev.Tasks;
using CouncilTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Tests;

public sealed class DevelopmentTaskQueueServiceTests
{
    [Fact]
    public async Task CreateDoesNotApproveAndTasksAreVisibleAcrossServiceInstances()
    {
        var path = CreatePath();
        try
        {
            var first = new DevelopmentTaskQueueService(path);
            var created = await first.CreateAsync(
                "Build the first-use dashboard",
                "Keep task creation separate from approval and execution.");

            Assert.Equal(CouncilTaskStatus.Pending, created.Status);
            Assert.False(created.IsApproved);

            var second = new DevelopmentTaskQueueService(path);
            var saved = Assert.Single(await second.ListAsync());
            Assert.Equal(created.Id, saved.Id);
            Assert.Equal(created.Title, saved.Title);
            Assert.Equal(created.Description, saved.Description);
            Assert.False(saved.IsApproved);
        }
        finally
        {
            DeleteStore(path);
        }
    }

    [Fact]
    public async Task ApprovalCanBeRevokedBeforeTheBackgroundWorkerClaimsIt()
    {
        var path = CreatePath();
        try
        {
            var service = new DevelopmentTaskQueueService(path);
            var created = await service.CreateAsync("Review task", "Approval should be reversible before claim.");

            Assert.True(await service.ApproveAsync(created.Id));
            var approved = Assert.Single(await service.ListAsync());
            Assert.True(approved.IsApproved);
            Assert.Null(approved.ApprovalConsumedAtUtc);

            Assert.True(await service.RevokeApprovalAsync(created.Id));
            var revoked = Assert.Single(await new DevelopmentTaskQueueService(path).ListAsync());
            Assert.False(revoked.IsApproved);
            Assert.Null(revoked.ApprovalConsumedAtUtc);
        }
        finally
        {
            DeleteStore(path);
        }
    }

    [Fact]
    public async Task ConsumedApprovalCannotBeRevokedFromDesktopQueue()
    {
        var path = CreatePath();
        try
        {
            var service = new DevelopmentTaskQueueService(path);
            var created = await service.CreateAsync("Claimed task", "The worker has already claimed this task.");
            Assert.True(await service.ApproveAsync(created.Id));

            var workerView = new TaskManager(new TaskStore(path));
            await workerView.InitializeAsync();
            var claimed = await workerView.ClaimNextApprovedTaskAsync();
            Assert.NotNull(claimed);
            Assert.Equal(created.Id, claimed!.Id);

            Assert.False(await service.RevokeApprovalAsync(created.Id));
            var saved = Assert.Single(await service.ListAsync());
            Assert.True(saved.IsApproved);
            Assert.NotNull(saved.ApprovalConsumedAtUtc);
        }
        finally
        {
            DeleteStore(path);
        }
    }

    private static string CreatePath() =>
        Path.Combine(Path.GetTempPath(), "jarvis-desktop-task-queue-" + Guid.NewGuid().ToString("N") + ".json");

    private static void DeleteStore(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        if (File.Exists(path + ".lock"))
            File.Delete(path + ".lock");
    }
}
