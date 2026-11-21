using Council.App.Chat;
using Council.Memory;
using Xunit;

namespace Council.Dev.Tests;

public class LearningSummaryTests
{
    private static readonly DateTimeOffset SessionStart = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("What did you learn from the task we just completed?")]
    [InlineData("what have you learned")]
    [InlineData("/learned")]
    [InlineData("  What did you learn?  ")]
    public void RecognisesLearningQuestions(string prompt) =>
        Assert.True(LearningSummary.IsLearningQuestion(prompt));

    [Theory]
    [InlineData("Teach me what you learned about beams in IS 456")]
    [InlineData("/learn x => y")]
    public void IgnoresOtherPrompts(string prompt) =>
        Assert.False(LearningSummary.IsLearningQuestion(prompt));

    [Fact]
    public void EmptyMemory_ExplainsHowToTeach()
    {
        var text = LearningSummary.Build(Array.Empty<(string, DateTimeOffset, string)>(), Array.Empty<LearnedInstinct>(), SessionStart);
        Assert.Contains("/remember", text);
        Assert.Contains("/learn", text);
    }

    [Fact]
    public void SeparatesNewItemsAndExplainsEffect()
    {
        var facts = new List<(string, DateTimeOffset, string)>
        {
            ("f1", SessionStart.AddDays(-3), "I prefer SI units"),
            ("f2", SessionStart.AddMinutes(5), "My MTech research is on aluminium")
        };
        var practices = new List<LearnedInstinct>
        {
            new("p1", SessionStart.AddDays(-1), SessionStart.AddMinutes(10), "a beam design question", "use the concrete_beam_flexure tool", "civil", 0.6, 2)
        };

        var text = LearningSummary.Build(facts, practices, SessionStart);

        var newSection = text[..text.IndexOf("Everything I keep:", StringComparison.Ordinal)];
        Assert.Contains("Remembered: My MTech research is on aluminium", newSection);
        Assert.DoesNotContain("SI units", newSection);
        Assert.Contains("Reinforced practice", newSection);
        Assert.Contains("[f1] I prefer SI units", text);
        Assert.Contains("How this affects future tasks:", text);
        Assert.Contains("matches its trigger", text);
    }

    [Fact]
    public void NothingNew_SaysSo()
    {
        var facts = new List<(string, DateTimeOffset, string)> { ("f1", SessionStart.AddDays(-3), "I prefer SI units") };
        var text = LearningSummary.Build(facts, Array.Empty<LearnedInstinct>(), SessionStart);
        Assert.StartsWith("Nothing new was learned in this session.", text);
    }
}
