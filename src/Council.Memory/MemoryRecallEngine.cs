using System.Text;

namespace Council.Memory;

/// <summary>
/// Deterministic, offline relevance ranking for local conversation history,
/// explicit facts and user-approved project practices. No embeddings or network calls.
/// </summary>
public sealed record MemoryRecallHit<T>(
    T Item,
    double Score,
    int MatchedTerms,
    int QueryTerms);

public static class MemoryRecallEngine
{
    private const double K1 = 1.2;
    private const double LengthNormalization = 0.25;
    private const double MaximumFreshnessBoost = 0.05;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "about", "above", "after", "again", "all", "also", "am", "an", "and",
        "any", "are", "as", "at", "be", "because", "been", "before", "being", "below",
        "between", "both", "but", "by", "can", "could", "did", "do", "does", "doing",
        "down", "during", "each", "few", "for", "from", "further", "had", "has", "have",
        "having", "he", "her", "here", "hers", "herself", "him", "himself", "his", "how",
        "i", "if", "in", "into", "is", "it", "its", "itself", "just", "me", "more",
        "most", "my", "myself", "no", "nor", "not", "now", "of", "off", "on", "once",
        "only", "or", "other", "our", "ours", "ourselves", "out", "over", "own", "please",
        "same", "she", "should", "so", "some", "such", "than", "that", "the", "their",
        "theirs", "them", "themselves", "then", "there", "these", "they", "this", "those",
        "through", "to", "too", "under", "until", "up", "us", "very", "was", "we", "were",
        "what", "when", "where", "which", "while", "who", "whom", "why", "will", "with",
        "would", "you", "your", "yours", "yourself", "yourselves"
    };

    /// <summary>
    /// Ranks candidate text using BM25-style inverse document frequency, light
    /// phrase/proximity bonuses, optional recency and optional confidence.
    /// Technical numbers and identifiers are retained as tokens.
    /// </summary>
    public static IReadOnlyList<MemoryRecallHit<T>> Rank<T>(
        string? query,
        IEnumerable<T> candidates,
        Func<T, string?> contentSelector,
        Func<T, DateTimeOffset?>? timestampSelector = null,
        Func<T, double>? confidenceSelector = null,
        int maxResults = 8,
        double minimumScore = 0.12)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(contentSelector);
        if (maxResults is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        if (!double.IsFinite(minimumScore) || minimumScore < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumScore));

        var queryTokens = Tokenize(query)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (queryTokens.Length == 0)
            return Array.Empty<MemoryRecallHit<T>>();

        var documents = new List<Document<T>>();
        foreach (var item in candidates)
        {
            var text = contentSelector(item);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var tokens = Tokenize(text);
            if (tokens.Length == 0)
                continue;

            documents.Add(new Document<T>(
                item,
                tokens,
                tokens.GroupBy(token => token, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                timestampSelector?.Invoke(item),
                confidenceSelector?.Invoke(item)));
        }

        if (documents.Count == 0)
            return Array.Empty<MemoryRecallHit<T>>();

        var documentFrequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in queryTokens)
        {
            documentFrequencies[term] = documents.Count(document => document.TermFrequencies.ContainsKey(term));
        }

        var averageLength = Math.Max(1d, documents.Average(document => document.Tokens.Length));
        var now = DateTimeOffset.UtcNow;
        var hits = new List<MemoryRecallHit<T>>();

        foreach (var document in documents)
        {
            var matchedTerms = 0;
            var bm25 = 0d;

            foreach (var term in queryTokens)
            {
                if (!document.TermFrequencies.TryGetValue(term, out var frequency))
                    continue;

                matchedTerms++;
                var documentFrequency = documentFrequencies[term];
                var inverseDocumentFrequency = Math.Log(
                    1d + (documents.Count - documentFrequency + 0.5d) /
                    (documentFrequency + 0.5d));
                var lengthFactor = LengthNormalization +
                    (1d - LengthNormalization) * document.Tokens.Length / averageLength;
                var termScore = frequency * (K1 + 1d) / (frequency + K1 * lengthFactor);
                bm25 += inverseDocumentFrequency * termScore;
            }

            if (matchedTerms == 0)
                continue;

            var coverage = (double)matchedTerms / queryTokens.Length;
            var score = bm25 * (0.70d + 0.30d * coverage);
            score += ProximityBonus(queryTokens, document.Tokens);

            if (ContainsOrderedPhrase(queryTokens, document.Tokens))
                score += 0.45d;

            if (document.Timestamp is { } timestamp)
            {
                var ageDays = Math.Max(0d, (now - timestamp).TotalDays);
                score += MaximumFreshnessBoost * Math.Exp(-ageDays / 365d);
            }

            if (document.Confidence is { } confidence)
                score *= 0.90d + 0.10d * Math.Clamp(confidence, 0d, 1d);

            if (score >= minimumScore)
                hits.Add(new MemoryRecallHit<T>(document.Item, score, matchedTerms, queryTokens.Length));
        }

        return hits
            .OrderByDescending(hit => hit.Score)
            .ThenByDescending(hit => timestampSelector?.Invoke(hit.Item))
            .Take(maxResults)
            .ToArray();
    }

    private static double ProximityBonus(IReadOnlyList<string> queryTokens, IReadOnlyList<string> documentTokens)
    {
        if (queryTokens.Count < 2 || documentTokens.Count < 2)
            return 0d;

        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < documentTokens.Count; index++)
        {
            var token = documentTokens[index];
            if (!positions.TryGetValue(token, out var indices))
            {
                indices = [];
                positions[token] = indices;
            }
            indices.Add(index);
        }

        var bonus = 0d;
        for (var index = 1; index < queryTokens.Count; index++)
        {
            if (!positions.TryGetValue(queryTokens[index - 1], out var left) ||
                !positions.TryGetValue(queryTokens[index], out var right))
                continue;

            var closestDistance = int.MaxValue;
            foreach (var leftPosition in left)
            {
                foreach (var rightPosition in right)
                {
                    var distance = Math.Abs(rightPosition - leftPosition);
                    if (distance < closestDistance)
                        closestDistance = distance;
                }
            }

            if (closestDistance is > 0 and <= 3)
                bonus += 0.12d / closestDistance;
        }

        return Math.Min(0.45d, bonus);
    }

    private static bool ContainsOrderedPhrase(
        IReadOnlyList<string> queryTokens,
        IReadOnlyList<string> documentTokens)
    {
        if (queryTokens.Count < 2 || queryTokens.Count > documentTokens.Count)
            return false;

        for (var start = 0; start <= documentTokens.Count - queryTokens.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < queryTokens.Count; offset++)
            {
                if (!string.Equals(
                    queryTokens[offset],
                    documentTokens[start + offset],
                    StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static string[] Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var result = new List<string>();
        var token = new StringBuilder();

        void FlushToken()
        {
            if (token.Length == 0)
                return;

            var normalized = NormalizeToken(token.ToString());
            if (normalized.Length > 0)
                result.Add(normalized);
            token.Clear();
        }

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                token.Append(char.ToLowerInvariant(character));
            else
                FlushToken();
        }
        FlushToken();

        return result.ToArray();
    }

    private static string NormalizeToken(string token)
    {
        // Light English suffix normalization helps terms such as bending/bend,
        // mullions/mullion and fins/fin match without altering numeric identifiers.
        if (token.Length > 5 && token.EndsWith("ies", StringComparison.Ordinal))
            return token[..^3] + "y";

        if (token.Length > 5 && token.EndsWith("ing", StringComparison.Ordinal))
        {
            var stem = token[..^3];
            if (stem.Length > 2 && stem[^1] == stem[^2])
                stem = stem[..^1];
            return stem;
        }

        if (token.Length > 4 && token.EndsWith("ed", StringComparison.Ordinal))
        {
            var stem = token[..^2];
            if (stem.Length > 2 && stem[^1] == stem[^2])
                stem = stem[..^1];
            return stem;
        }

        if (token.Length > 4 &&
            token.EndsWith("s", StringComparison.Ordinal) &&
            !token.EndsWith("ss", StringComparison.Ordinal) &&
            !token.EndsWith("is", StringComparison.Ordinal))
            return token[..^1];

        return token;
    }

    private sealed record Document<T>(
        T Item,
        string[] Tokens,
        Dictionary<string, int> TermFrequencies,
        DateTimeOffset? Timestamp,
        double? Confidence);
}
