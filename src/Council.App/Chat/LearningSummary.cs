using System.Text;
using Council.Memory;

namespace Council.App.Chat;

/// <summary>
/// Builds the plain-language answer to "What did you learn?": what JARVIS has saved,
/// what is new since a given time, and how each kind of item affects future replies.
/// </summary>
public static class LearningSummary
{
    /// <summary>True for the questions the spec lists, e.g. "What did you learn from this task?".</summary>
    public static bool IsLearningQuestion(string prompt)
    {
        var text = prompt.Trim().TrimEnd('?', '.', '!').ToLowerInvariant();
        return text is "/learned" or "/what-did-you-learn"
            || text.StartsWith("what did you learn", StringComparison.Ordinal)
            || text.StartsWith("what have you learned", StringComparison.Ordinal)
            || text.StartsWith("what have you learnt", StringComparison.Ordinal)
            || text.StartsWith("what did you learnt", StringComparison.Ordinal);
    }

    public static string Build(
        IReadOnlyList<(string Id, DateTimeOffset CreatedAt, string Fact)> facts,
        IReadOnlyList<LearnedInstinct> practices,
        DateTimeOffset recentSince)
    {
        if (facts.Count == 0 && practices.Count == 0)
        {
            return "I haven't learned anything to keep yet. Teach me with /remember <fact> for a personal preference, " +
                   "or /learn <when> => <what to do> for a way of working. I only keep what you explicitly teach me.";
        }

        var recentFacts = facts.Where(f => f.CreatedAt >= recentSince).ToList();
        var recentPractices = practices.Where(p => p.UpdatedAt >= recentSince).ToList();
        var text = new StringBuilder();

        if (recentFacts.Count + recentPractices.Count == 0)
            text.AppendLine("Nothing new was learned in this session. Here is everything I currently keep:");
        else
        {
            text.AppendLine("New in this session:");
            foreach (var fact in recentFacts)
                text.AppendLine($"- Remembered: {fact.Fact}");
            foreach (var practice in recentPractices)
                text.AppendLine(practice.EvidenceCount > 1
                    ? $"- Reinforced practice: when {practice.Trigger}, {practice.Action} (seen {practice.EvidenceCount} times)"
                    : $"- New practice: when {practice.Trigger}, {practice.Action}");
            text.AppendLine();
            text.AppendLine("Everything I keep:");
        }

        if (facts.Count > 0)
        {
            text.AppendLine($"Facts and preferences ({facts.Count}):");
            foreach (var fact in facts.OrderByDescending(f => f.CreatedAt))
                text.AppendLine($"  [{fact.Id}] {fact.Fact}");
        }

        if (practices.Count > 0)
        {
            text.AppendLine($"Ways of working ({practices.Count}):");
            foreach (var practice in practices.OrderByDescending(p => p.Confidence).ThenByDescending(p => p.UpdatedAt))
                text.AppendLine($"  [{practice.Id}] When {practice.Trigger}: {practice.Action} (confidence {practice.Confidence:0.00})");
        }

        text.AppendLine();
        text.AppendLine("How this affects future tasks:");
        if (facts.Count > 0)
            text.AppendLine("- Facts and preferences are given to me as context with every request.");
        if (practices.Count > 0)
            text.AppendLine("- A way of working is applied only when a new request matches its trigger, and never overrides what you ask at the time.");
        text.Append("To change something: /forget <id> removes a fact, /forget-instinct <id> removes a practice, and teaching it again with the corrected wording replaces it.");
        return text.ToString();
    }
}
