using Council.AI.Providers;
using Council.Core.Models;

namespace Council.Dev.Tests;

public sealed class ClaudeProviderTests
{
    [Fact]
    public void SystemMessagesBecomeTheSystemPromptAndTurnsKeepTheirOrder()
    {
        var (system, turns) = ClaudeProvider.BuildConversation(new AIRequest(new[]
        {
            new AIMessage("system", "You are JARVIS."),
            new AIMessage("user", "Hello"),
            new AIMessage("assistant", "Hi."),
            new AIMessage("user", "Open my notes"),
        }));

        Assert.Equal("You are JARVIS.", system);
        Assert.Equal(new[] { "user", "assistant", "user" }, turns.Select(t => t.Role));
        Assert.Equal("Open my notes", turns[^1].Text);
    }

    [Fact]
    public void TrailingAssistantMessageIsDroppedAndRepeatedRolesAreJoined()
    {
        var (_, turns) = ClaudeProvider.BuildConversation(new AIRequest(new[]
        {
            new AIMessage("user", "First"),
            new AIMessage("user", "Second"),
            new AIMessage("assistant", "Partial answer"),
        }));

        var only = Assert.Single(turns);
        Assert.Equal("user", only.Role);
        Assert.Equal("First\n\nSecond", only.Text);
    }

    [Fact]
    public void RequiresAUserMessage()
    {
        Assert.Throws<ArgumentException>(() => ClaudeProvider.BuildConversation(
            new AIRequest(new[] { new AIMessage("system", "Only rules") })));
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeProvider(" "));
    }
}
