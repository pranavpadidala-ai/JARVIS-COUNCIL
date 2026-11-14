using Council.Memory;

namespace Council.Dev.Tests;

public sealed class MemoryRecallEngineTests
{
    private sealed record RecallItem(string Text, DateTimeOffset UpdatedAt, double Confidence = 0.6);

    [Fact]
    public void RanksStrongTechnicalOverlapAheadOfGenericOverlap()
    {
        var now = DateTimeOffset.UtcNow;
        var items = new[]
        {
            new RecallItem(
                "Our aluminium facade baseline uses external fins and mullions under reversible wind pressure.",
                now),
            new RecallItem(
                "We discussed general aluminium facade construction and building performance.",
                now),
            new RecallItem(
                "The project stores local notes and presentation files.",
                now)
        };

        var results = MemoryRecallEngine.Rank(
            "aluminium facade fins mullions wind pressure",
            items,
            item => item.Text,
            item => item.UpdatedAt);

        Assert.NotEmpty(results);
        Assert.Contains("external fins and mullions", results[0].Item.Text);
        Assert.True(results[0].MatchedTerms >= 5);
    }

    [Fact]
    public void MatchesCommonEngineeringInflectionsAndNumericIdentifiers()
    {
        var item = new RecallItem(
            "Mullions bending test was checked against IS 1893 and the wind pressure envelope.",
            DateTimeOffset.UtcNow);

        var results = MemoryRecallEngine.Rank(
            "mullion bending 1893",
            new[] { item },
            candidate => candidate.Text);

        Assert.Single(results);
        Assert.Equal(item, results[0].Item);
        Assert.Equal(3, results[0].MatchedTerms);
    }

    [Fact]
    public void PhraseAndNearbyTermsImproveRanking()
    {
        var now = DateTimeOffset.UtcNow;
        var exact = new RecallItem("The report verifies wind pressure on the facade.", now);
        var separated = new RecallItem(
            "The wind changed direction during the experiment; the measured pressure was recorded later.",
            now);

        var results = MemoryRecallEngine.Rank(
            "wind pressure",
            new[] { separated, exact },
            item => item.Text);

        Assert.Equal(exact, results[0].Item);
    }

    [Fact]
    public void RecentMemoryBreaksOtherwiseEqualScores()
    {
        var now = DateTimeOffset.UtcNow;
        var recent = new RecallItem("Abaqus shell element buckling model.", now);
        var old = new RecallItem("Abaqus shell element buckling model.", now.AddYears(-3));

        var results = MemoryRecallEngine.Rank(
            "Abaqus shell element buckling",
            new[] { old, recent },
            item => item.Text,
            item => item.UpdatedAt);

        Assert.Equal(recent, results[0].Item);
    }

    [Fact]
    public void ConfidenceOnlyRefinesRelevantPracticeRanking()
    {
        var now = DateTimeOffset.UtcNow;
        var relevant = new RecallItem("When I ask for four point bending, confirm support span.", now, 0.6);
        var unrelatedHighConfidence = new RecallItem("When I ask about clothing, use a casual outfit.", now, 0.95);

        var results = MemoryRecallEngine.Rank(
            "prepare a four-point bending setup",
            new[] { unrelatedHighConfidence, relevant },
            item => item.Text,
            item => item.UpdatedAt,
            item => item.Confidence);

        Assert.Single(results);
        Assert.Equal(relevant, results[0].Item);
    }

    [Fact]
    public void ReturnsNoResultsForStopWordsAndUnrelatedText()
    {
        var items = new[]
        {
            new RecallItem("The facade uses aluminium fins and mullions.", DateTimeOffset.UtcNow),
            new RecallItem("Abaqus buckling analysis uses shell elements.", DateTimeOffset.UtcNow)
        };

        Assert.Empty(MemoryRecallEngine.Rank(
            "the and with",
            items,
            item => item.Text));

        Assert.Empty(MemoryRecallEngine.Rank(
            "unrelated astronomy telescope",
            items,
            item => item.Text));
    }

    [Fact]
    public void HonorsResultLimitAndMinimumScore()
    {
        var items = Enumerable.Range(0, 10)
            .Select(index => new RecallItem(
                $"The facade has an aluminium mullion and fin component {index}.",
                DateTimeOffset.UtcNow.AddMinutes(index)))
            .ToArray();

        var limited = MemoryRecallEngine.Rank(
            "aluminium mullion fin facade",
            items,
            item => item.Text,
            item => item.UpdatedAt,
            maxResults: 3);

        Assert.Equal(3, limited.Count);
        Assert.All(limited, hit => Assert.True(hit.Score >= 0.12));

        var filtered = MemoryRecallEngine.Rank(
            "astronomy telescope",
            items,
            item => item.Text,
            minimumScore: 100);

        Assert.Empty(filtered);
    }
}
