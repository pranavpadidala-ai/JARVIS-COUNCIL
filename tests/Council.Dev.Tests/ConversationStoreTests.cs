using Council.App.Chat;
using Council.Core.Models;

namespace Council.Dev.Tests;

public sealed class ConversationStoreTests
{
    [Fact]
    public async Task Saves_and_restores_chat_history()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"jarvis-chat-{Guid.NewGuid():N}.json");
        var store = new ConversationStore(path);
        var expected = new[]
        {
            new AIMessage("user", "Remember this"),
            new AIMessage("assistant", "I have it in this conversation.")
        };

        try
        {
            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
        }
        finally
        {
            await store.ClearAsync();
        }
    }

    [Fact]
    public async Task Keeps_only_the_most_recent_120_messages()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"jarvis-chat-{Guid.NewGuid():N}.json");
        var store = new ConversationStore(path);
        var messages = Enumerable.Range(0, 125)
            .Select(index => new AIMessage("user", index.ToString()))
            .ToArray();

        try
        {
            await store.SaveAsync(messages);
            var actual = await store.LoadAsync();

            Assert.Equal(120, actual.Count);
            Assert.Equal("5", actual[0].Content);
            Assert.Equal("124", actual[^1].Content);
        }
        finally
        {
            await store.ClearAsync();
        }
    }
}
