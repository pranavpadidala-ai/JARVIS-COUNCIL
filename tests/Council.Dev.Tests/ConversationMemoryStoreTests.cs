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
    public async Task SearchRelevant_ReturnsOlderMessagesWithKeywordOverlap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jarvis-memory-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "conversation.jsonl");
        try
        {
            var store = new ConversationMemoryStore(path);
            await store.AppendAsync(new AIMessage(
                "user",
                "My aluminium facade project uses external fins and mullions under reversible wind pressure."));
            await store.AppendAsync(new AIMessage(
                "assistant",
                "We also discussed the local storage format."));
            await store.AppendAsync(new AIMessage(
                "system",
                "This system-only message must never be searchable."));

            var results = await store.SearchRelevantAsync(
                "What did we discuss about aluminium fins and wind pressure?");

            Assert.Single(results);
            Assert.Equal("user", results[0].Role);
            Assert.Contains("reversible wind pressure", results[0].Content);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SearchRelevant_ReturnsEmptyForBlankOrUnmatchedQuery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jarvis-memory-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "conversation.jsonl");
        try
        {
            var store = new ConversationMemoryStore(path);
            await store.AppendAsync(new AIMessage("user", "The facade uses aluminium fins."));

            Assert.Empty(await store.SearchRelevantAsync("the and or"));
            Assert.Empty(await store.SearchRelevantAsync("unrelated astronomy topic"));
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
