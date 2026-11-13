using Council.AI.Providers;
using Council.App;
using Council.Core.Models;
using Council.Memory;

namespace Council.Dev.Tests;

public sealed class DesktopToolCallingAssistantTests
{
    [Fact]
    public async Task ProcessMessageAsync_uses_shared_tool_runtime_and_persists_conversation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), "jarvis-desktop-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var memory = new ConversationMemoryStore(Path.Combine(directory, "conversation.jsonl"));
            var assistant = new ToolCallingAssistant(new MockAIProvider(), directory, memory);

            var response = await assistant.ProcessMessageAsync("hello from desktop");

            Assert.Contains("Mock response received for: hello from desktop", response);
            var history = await memory.LoadRecentAsync(10);
            Assert.Equal(2, history.Count);
            Assert.Equal("user", history[0].Role);
            Assert.Equal("hello from desktop", history[0].Content);
            Assert.Equal("assistant", history[1].Role);
            Assert.Contains("Mock response received", history[1].Content);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ClearConversationAsync_removes_history_and_explicit_facts()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), "jarvis-desktop-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var memory = new ConversationMemoryStore(Path.Combine(directory, "conversation.jsonl"));
            var facts = new MemoryFactStore(memory.FilePath + ".facts.jsonl");
            await facts.AddAsync("Keep this only until clear is requested.");
            var assistant = new ToolCallingAssistant(new MockAIProvider(), directory, memory);
            await assistant.ProcessMessageAsync("hello");

            await assistant.ClearConversationAsync();

            Assert.Empty(await memory.LoadRecentAsync());
            Assert.Empty(await facts.ListAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
