using Council.Dev.Browser;

namespace Council.Dev.Tests;

public sealed class BraveProfileManagerTests
{
    [Fact]
    public async Task Profiles_have_distinct_isolated_data_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-brave-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var manager = new BraveProfileManager(root, Path.Combine(root, "brave.exe"));
            await manager.InitializeAsync();
            var personal = await manager.CreateProfileAsync("Personal", "Personal browsing");
            var research = await manager.CreateProfileAsync("Research", "Research and public sources");

            Assert.NotEqual(personal.DataDirectory, research.DataDirectory);
            Assert.True(Directory.Exists(personal.DataDirectory));
            Assert.True(Directory.Exists(research.DataDirectory));
            Assert.Equal(2, (await manager.ListProfilesAsync()).Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Persistent_profiles_cannot_be_removed_by_temporary_cleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-brave-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var manager = new BraveProfileManager(root);
            await manager.InitializeAsync();
            var profile = await manager.CreateProfileAsync("Work", "Work browser");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.DeleteTemporaryProfileAsync(profile.Id));
            var temporary = await manager.CreateProfileAsync("Temporary", "One-off public research", true);
            await manager.DeleteTemporaryProfileAsync(temporary.Id);
            Assert.Single(await manager.ListProfilesAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Invalid_start_urls_are_rejected_before_launch()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-brave-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var manager = new BraveProfileManager(root, Path.Combine(root, "missing-brave.exe"));
            await manager.InitializeAsync();
            var profile = await manager.CreateProfileAsync("Test", "URL validation");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                manager.LaunchProfileAsync(profile.Id, BraveProfileMode.Headless, "javascript:alert(1)"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}