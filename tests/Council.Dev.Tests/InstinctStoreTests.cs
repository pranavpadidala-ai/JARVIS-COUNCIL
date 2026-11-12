using Council.Memory;

namespace Council.Dev.Tests;

public sealed class InstinctStoreTests
{
    [Fact]
    public async Task AddAndReinforce_StoresEncryptedProjectPracticeWithConfidence()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "jarvis-instincts-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "project-a");
        Directory.CreateDirectory(workspace);
        try
        {
            var store = new InstinctStore(workspace, Path.Combine(root, "store"));
            var first = await store.AddOrReinforceAsync("when writing tests", "Add a regression test first", "testing");
            var second = await store.AddOrReinforceAsync("when writing tests", "Add focused regression tests first", "testing");

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(0.65, second.Confidence);
            Assert.Equal(2, second.EvidenceCount);
            Assert.Equal("Add focused regression tests first", second.Action);

            var persisted = string.Join("", Directory.GetFiles(Path.Combine(root, "store")).Select(File.ReadAllText));
            Assert.DoesNotContain("Add focused regression tests first", persisted);
            var listed = Assert.Single(await store.ListAsync());
            Assert.Equal(second.Id, listed.Id);
            Assert.True(await store.RemoveAsync(second.Id));
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PracticesAreIsolatedByWorkspacePath()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "jarvis-instinct-scope-" + Guid.NewGuid().ToString("N"));
        var firstWorkspace = Path.Combine(root, "project-a");
        var secondWorkspace = Path.Combine(root, "project-b");
        Directory.CreateDirectory(firstWorkspace);
        Directory.CreateDirectory(secondWorkspace);
        try
        {
            var first = new InstinctStore(firstWorkspace, Path.Combine(root, "store"));
            var second = new InstinctStore(secondWorkspace, Path.Combine(root, "store"));
            await first.AddOrReinforceAsync("when debugging", "Reproduce before changing code");
            Assert.Single(await first.ListAsync());
            Assert.Empty(await second.ListAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AddRejectsOversizedPractice()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-instinct-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new InstinctStore(root, Path.Combine(root, "store"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => store.AddOrReinforceAsync(new string('x', 161), "Action"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
