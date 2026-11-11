using Council.Core.Models;
using Council.Memory;

namespace Council.Dev.Tests;

public sealed class ConversationMemoryStoreTests
{
    [Fact]
    public async Task AppendAndLoad_PersistsOnlyConversationMessages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jarvis-memory-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "conversation.jsonl");
        try
        {
            var store = new ConversationMemoryStore(path);
            await store.AppendAsync(new AIMessage("system", "Do not persist this."));
            await store.AppendAsync(new AIMessage("user", "Remember this question."));
            await store.AppendAsync(new AIMessage("assistant", "I will remember it."));

            var loaded = await store.LoadRecentAsync(25);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("user", loaded[0].Role);
            Assert.Equal("Remember this question.", loaded[0].Content);
            Assert.Equal("assistant", loaded[1].Role);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadRecent_ReturnsOnlyRequestedNumberOfMessages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jarvis-memory-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "conversation.jsonl");
        try
        {
            var store = new ConversationMemoryStore(path);
            for (var index = 0; index < 5; index++)
                await store.AppendAsync(new AIMessage("user", "message-" + index));

            var loaded = await store.LoadRecentAsync(2);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("message-3", loaded[0].Content);
            Assert.Equal("message-4", loaded[1].Content);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Clear_RemovesPersistedConversation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jarvis-memory-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "conversation.jsonl");
        try
        {
            var store = new ConversationMemoryStore(path);
            await store.AppendAsync(new AIMessage("user", "clear me"));
            await store.ClearAsync();

            Assert.Empty(await store.LoadRecentAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
