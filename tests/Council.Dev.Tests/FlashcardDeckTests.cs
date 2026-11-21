using Council.App.Learning;
using Xunit;

namespace Council.Dev.Tests;

public sealed class FlashcardDeckTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-cards-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly DateOnly _today = new(2026, 11, 22);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void CorrectAnswersSpaceOutAndWrongOnesComeBackTomorrow()
    {
        var deck = new FlashcardDeck(_path);
        var card = deck.Add("Hindi", "पानी", "paani - water", _today);
        Assert.Equal("hindi", card.Deck);
        Assert.Single(deck.Due("hindi", _today, 10));

        var up = deck.Answer(card.Id, true, _today);
        Assert.Equal(2, up.Box);
        Assert.Equal(_today.AddDays(2), up.DueOn);
        Assert.Empty(deck.Due(null, _today, 10));

        var down = deck.Answer(card.Id, false, _today.AddDays(2));
        Assert.Equal(1, down.Box);
        Assert.Equal(_today.AddDays(3), down.DueOn);
        Assert.Equal(1, down.Lapses);
    }

    [Fact]
    public void DoesNotDuplicateCards()
    {
        var deck = new FlashcardDeck(_path);
        var a = deck.Add("telugu", "నీళ్ళు", "neellu - water", _today);
        var b = deck.Add("telugu", "నీళ్ళు", "water", _today);
        Assert.Equal(a.Id, b.Id);
        Assert.Contains("telugu: 1 cards", deck.Stats(_today));
    }

    [Fact]
    public async Task ToolRunsAQuiz()
    {
        var tool = new FlashcardTool(new FlashcardDeck(_path), () => _today);
        var add = await tool.ExecuteAsync(new() { ["action"] = "add", ["deck"] = "german", ["front"] = "die Bemessung", ["back"] = "design (structural)" });
        Assert.True(add.Success);
        var quiz = await tool.ExecuteAsync(new() { ["action"] = "quiz", ["deck"] = "german" });
        Assert.Contains("die Bemessung", quiz.Message);
        var id = quiz.Message.Split("id ")[1].Split(' ')[0];
        var answer = await tool.ExecuteAsync(new() { ["action"] = "answer", ["id"] = id, ["correct"] = "true" });
        Assert.Contains("Correct", answer.Message);
    }
}
