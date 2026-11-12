using Council.Memory;

namespace Council.Dev.Tests;

public sealed class MemoryFactStoreTests
{
    [Fact]
    public async Task AddListAndRemove_ManageExplicitFactsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "jarvis-facts-" + Guid.NewGuid().ToString("N") + ".jsonl");
        const string fact = "The user prefers local-first tools.";
        try
        {
            var store = new MemoryFactStore(path);
            var id = await store.AddAsync(fact);

            var persisted = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain(fact, persisted);

            var facts = await store.ListAsync();
            var saved = Assert.Single(facts);
            Assert.Equal(id, saved.Id);
            Assert.Equal(fact, saved.Fact);

            Assert.True(await store.RemoveAsync(id));
            Assert.Empty(await store.ListAsync());
            Assert.False(await store.RemoveAsync(id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Add_RejectsFactsLongerThanTwoThousandCharacters()
    {
        var path = Path.Combine(Path.GetTempPath(), "jarvis-facts-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var store = new MemoryFactStore(path);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => store.AddAsync(new string('x', 2001)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
