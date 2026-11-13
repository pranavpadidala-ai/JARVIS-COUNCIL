using Council.Dev.Recovery;
using Council.Dev.Tasks;
using DevTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Dev.Tests;

public sealed class TaskCheckpointRecoveryTests
{
    [Fact]
    public async Task Running_task_is_restored_as_paused_from_its_latest_checkpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var taskFile = Path.Combine(root, "tasks.json");
        var checkpointRoot = Path.Combine(root, "checkpoints");
        try
        {
            var first = new TaskManager(new TaskStore(taskFile), new TaskCheckpointStore(checkpointRoot));
            await first.InitializeAsync();
            var task = await first.CreateAsync("Implement recovery", "Persist task state across restart.");
            await first.UpdateStatusAsync(task.Id, DevTaskStatus.Running);
            await first.UpdateStepAsync(task.Id, "Applying the next code change.");
            var artifactPath = Path.Combine(root, "result.txt");
            await File.WriteAllTextAsync(artifactPath, "checkpointed output");
            var artifact = await TaskCheckpointStore.HashArtifactAsync(root, artifactPath);
            await first.UpdateArtifactsAsync(task.Id, [artifact]);

            var checkpoint = await new TaskCheckpointStore(checkpointRoot).LoadAsync(task.Id);
            Assert.NotNull(checkpoint);
            Assert.Equal("Applying the next code change.", checkpoint!.NextAction);
            Assert.NotEmpty(checkpoint.IdempotencyKey);
            Assert.Single(checkpoint.Artifacts);
            Assert.Equal("result.txt", checkpoint.Artifacts[0].RelativePath);

            var restarted = new TaskManager(new TaskStore(taskFile), new TaskCheckpointStore(checkpointRoot));
            await restarted.InitializeAsync();
            var recovered = restarted.Find(task.Id);

            Assert.NotNull(recovered);
            Assert.Equal(DevTaskStatus.Paused, recovered!.Status);
            Assert.Equal("Applying the next code change.", recovered.CurrentStep);
            Assert.Single(recovered.Artifacts);
            Assert.Equal(artifact.Sha256, recovered.Artifacts[0].Sha256);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Corrupt_checkpoint_does_not_prevent_task_queue_startup()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-recovery-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var taskFile = Path.Combine(root, "tasks.json");
        var checkpointRoot = Path.Combine(root, "checkpoints");
        try
        {
            var first = new TaskManager(new TaskStore(taskFile), new TaskCheckpointStore(checkpointRoot));
            await first.InitializeAsync();
            var task = await first.CreateAsync("Recover despite corrupt checkpoint", "Keep the queue usable.");
            await first.UpdateStatusAsync(task.Id, DevTaskStatus.Running);
            await File.WriteAllTextAsync(Path.Combine(checkpointRoot, task.Id.ToString("N") + ".json"), "{ invalid json");

            var restarted = new TaskManager(new TaskStore(taskFile), new TaskCheckpointStore(checkpointRoot));
            await restarted.InitializeAsync();
            var recovered = restarted.Find(task.Id);

            Assert.NotNull(recovered);
            Assert.Equal(DevTaskStatus.Paused, recovered!.Status);
            Assert.Equal("checkpoint_unreadable", recovered.FailureClassification);
            Assert.Contains("could not be read", recovered.CurrentStep);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Artifact_hash_is_scoped_to_workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "result.txt");
            await File.WriteAllTextAsync(file, "durable artifact");
            var snapshot = await TaskCheckpointStore.HashArtifactAsync(root, file);
            Assert.Equal("result.txt", snapshot.RelativePath);
            Assert.Equal(64, snapshot.Sha256.Length);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TaskCheckpointStore.HashArtifactAsync(root, Path.Combine(root, "..", "outside.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}