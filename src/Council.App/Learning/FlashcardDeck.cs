using System.Text.Json;

namespace Council.App.Learning;

public sealed record Flashcard(string Id, string Deck, string Front, string Back, int Box, DateOnly DueOn, int Reviews, int Lapses);

/// <summary>
/// Spaced-repetition flashcards (Leitner boxes 1-6: review after 1, 2, 4, 8, 16, 32 days). A correct
/// answer moves a card up a box, a wrong one sends it back to box 1. Stored only on this laptop.
/// </summary>
public sealed class FlashcardDeck
{
    private static readonly int[] IntervalDays = { 1, 2, 4, 8, 16, 32 };
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public FlashcardDeck(string? path = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("JARVIS_FLASHCARDS_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "flashcards.json");
    }

    public Flashcard Add(string deck, string front, string back, DateOnly today)
    {
        deck = Required(deck, "deck", 40).ToLowerInvariant();
        front = Required(front, "front", 300);
        back = Required(back, "back", 500);
        lock (FileLock)
        {
            var cards = Load();
            var existing = cards.FirstOrDefault(c => c.Deck == deck && c.Front.Equals(front, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            if (cards.Count >= 20000) throw new InvalidOperationException("The flashcard store is full (20,000 cards).");
            var card = new Flashcard(Guid.NewGuid().ToString("N")[..8], deck, front, back, 1, today, 0, 0);
            cards.Add(card);
            Save(cards);
            return card;
        }
    }

    public IReadOnlyList<Flashcard> Due(string? deck, DateOnly today, int max)
    {
        lock (FileLock)
        {
            return Load()
                .Where(c => c.DueOn <= today && (string.IsNullOrWhiteSpace(deck) || c.Deck == deck.Trim().ToLowerInvariant()))
                .OrderBy(c => c.Box).ThenBy(c => c.DueOn)
                .Take(Math.Clamp(max, 1, 50))
                .ToList();
        }
    }

    public Flashcard Answer(string id, bool correct, DateOnly today)
    {
        lock (FileLock)
        {
            var cards = Load();
            var index = cards.FindIndex(c => c.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new ArgumentException($"No flashcard with id {id}.");
            var card = cards[index];
            var box = correct ? Math.Min(card.Box + 1, IntervalDays.Length) : 1;
            var updated = card with
            {
                Box = box,
                DueOn = today.AddDays(correct ? IntervalDays[box - 1] : 1),
                Reviews = card.Reviews + 1,
                Lapses = card.Lapses + (correct ? 0 : 1)
            };
            cards[index] = updated;
            Save(cards);
            return updated;
        }
    }

    public string Stats(DateOnly today)
    {
        lock (FileLock)
        {
            var cards = Load();
            if (cards.Count == 0) return "No flashcards yet.";
            var lines = cards.GroupBy(c => c.Deck).OrderBy(g => g.Key).Select(g =>
                $"- {g.Key}: {g.Count()} cards, {g.Count(c => c.DueOn <= today)} due today, " +
                $"{g.Count(c => c.Box >= 4)} well learned (box 4+)");
            return string.Join("\n", lines);
        }
    }

    private static string Required(string value, string name, int max)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) throw new ArgumentException($"{name} is required.");
        return value.Length > max ? value[..max] : value;
    }

    private List<Flashcard> Load() =>
        File.Exists(_path) ? JsonSerializer.Deserialize<List<Flashcard>>(File.ReadAllText(_path), JsonOptions) ?? new() : new();

    private void Save(List<Flashcard> cards)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(cards, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}
