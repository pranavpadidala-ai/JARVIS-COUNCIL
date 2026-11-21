using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Learning;

/// <summary>Spaced-repetition flashcards for language learning (or any subject, such as code clauses).</summary>
public sealed class FlashcardTool : ITool
{
    private readonly FlashcardDeck _deck;
    private readonly Func<DateOnly> _today;

    public FlashcardTool(FlashcardDeck? deck = null, Func<DateOnly>? today = null)
    {
        _deck = deck ?? new FlashcardDeck();
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
    }

    public string Name => "flashcards";

    public string Description =>
        "Spaced-repetition flashcards for learning languages or any subject. Arguments: action = add | quiz | answer | stats. " +
        "add: deck (e.g. hindi, telugu, german, gre), front (word or question) and back (meaning, romanisation, example). " +
        "quiz: optional deck and max (default 10); returns due cards. Show only the front, let the user answer, then call " +
        "answer with id and correct=true|false. Correct answers come back later (1, 2, 4, 8, 16, 32 days); wrong ones tomorrow.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        var today = _today();
        try
        {
            switch (Arg("action").ToLowerInvariant())
            {
                case "add":
                    var card = _deck.Add(Arg("deck"), Arg("front"), Arg("back"), today);
                    return Done(true, $"Saved to {card.Deck} (id {card.Id}): {card.Front}");
                case "quiz":
                    var due = _deck.Due(Arg("deck"), today, int.TryParse(Arg("max"), out var m) ? m : 10);
                    return Done(true, due.Count == 0
                        ? "Nothing due today. Add new words or come back tomorrow."
                        : $"{due.Count} card(s) due. Ask one at a time, showing only the front:\n" +
                          string.Join("\n", due.Select(c => $"- id {c.Id} [{c.Deck}, box {c.Box}] front: {c.Front} | back: {c.Back}")));
                case "answer":
                    if (!bool.TryParse(Arg("correct"), out var correct)) return Done(false, "correct must be true or false.");
                    var updated = _deck.Answer(Arg("id"), correct, today);
                    return Done(true, correct
                        ? $"Correct. Next review {updated.DueOn:dd MMM} (box {updated.Box})."
                        : $"Not quite: {updated.Front} = {updated.Back}. It will come back tomorrow.");
                case "stats":
                    return Done(true, _deck.Stats(today));
                default:
                    return Done(false, "action must be add, quiz, answer or stats.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            return Done(false, ex.Message);
        }
    }

    private static Task<ToolResult> Done(bool ok, string message) => Task.FromResult(new ToolResult(ok, message));
}
