using System.Text;
using System.Text.RegularExpressions;

namespace Council.App.Knowledge;

public sealed record LibraryHit(string Document, int Page, double Score, string Excerpt);

/// <summary>
/// Keyword (BM25) search over the owner's private library of standards and books.
/// Each document is a .txt file with "=== PAGE n ===" markers, made from the owner's own PDFs
/// and kept outside the repository. Lightweight: no model, no embeddings, rebuilt only when files change.
/// </summary>
public sealed class StandardsLibrary
{
    private static readonly Regex PageMarker = new(@"^=== PAGE (\d+) ===\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex TokenPattern = new(@"[a-z0-9]+(?:\.[0-9]+)*", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "for", "is", "are", "be", "by", "with", "as", "at",
        "it", "this", "that", "what", "which", "how", "why", "does", "do", "from", "can", "per", "into", "than",
        "explain", "tell", "me", "about", "please", "according", "code", "clause"
    };
    private const double K1 = 1.4, B = 0.75;

    private readonly string _root;
    private readonly object _gate = new();
    private List<PageEntry> _pages = new();
    private Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);
    private double _averageLength;
    private string _signature = "";

    public StandardsLibrary(string root) => _root = root;

    public string Root => _root;

    public IReadOnlyList<(string Document, int Pages)> Documents()
    {
        EnsureIndex();
        return _pages.GroupBy(p => p.Document).Select(g => (g.Key, g.Count())).OrderBy(d => d.Key).ToList();
    }

    public IReadOnlyList<LibraryHit> Search(string query, int maxResults = 5, string? documentFilter = null)
    {
        EnsureIndex();
        var terms = Tokenize(query).Where(t => !StopWords.Contains(t)).Distinct().ToList();
        if (terms.Count == 0 || _pages.Count == 0) return Array.Empty<LibraryHit>();

        var candidates = string.IsNullOrWhiteSpace(documentFilter)
            ? _pages
            : _pages.Where(p => p.Document.Contains(documentFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var total = _pages.Count;

        var scored = new List<(PageEntry Page, double Score)>();
        foreach (var page in candidates)
        {
            double score = 0;
            var matched = 0;
            foreach (var term in terms)
            {
                if (!page.TermCounts.TryGetValue(term, out var tf)) continue;
                matched++;
                var df = _documentFrequency.GetValueOrDefault(term, 0);
                var idf = Math.Log(1 + (total - df + 0.5) / (df + 0.5));
                score += idf * tf * (K1 + 1) / (tf + K1 * (1 - B + B * page.Length / _averageLength));
            }
            if (matched == 0) continue;
            score *= 1 + 0.5 * (matched - 1) / Math.Max(1, terms.Count - 1); // reward pages covering more of the question
            scored.Add((page, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(Math.Clamp(maxResults, 1, 10))
            .Select(s => new LibraryHit(s.Page.Document, s.Page.Number, Math.Round(s.Score, 2), Excerpt(s.Page.Text, terms)))
            .ToList();
    }

    private void EnsureIndex()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_root))
            {
                _pages = new();
                _signature = "";
                return;
            }

            var files = Directory.EnumerateFiles(_root, "*.txt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var signature = string.Join("|", files.Select(f => f + ":" + new FileInfo(f).Length + ":" + File.GetLastWriteTimeUtc(f).Ticks));
            if (signature == _signature) return;

            var pages = new List<PageEntry>();
            foreach (var file in files)
                pages.AddRange(ReadPages(file, Path.GetFileNameWithoutExtension(file)));

            var df = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var page in pages)
                foreach (var term in page.TermCounts.Keys)
                    df[term] = df.GetValueOrDefault(term) + 1;

            _pages = pages;
            _documentFrequency = df;
            _averageLength = pages.Count == 0 ? 1 : Math.Max(1, pages.Average(p => p.Length));
            _signature = signature;
        }
    }

    private static IEnumerable<PageEntry> ReadPages(string file, string document)
    {
        var text = File.ReadAllText(file);
        var markers = PageMarker.Matches(text);
        if (markers.Count == 0)
        {
            yield return PageEntry.Create(document, 1, text);
            yield break;
        }
        for (var i = 0; i < markers.Count; i++)
        {
            var start = markers[i].Index + markers[i].Length;
            var end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
            var body = text[start..end];
            if (body.Trim().Length < 40) continue; // blank or watermark-only page
            yield return PageEntry.Create(document, int.Parse(markers[i].Groups[1].Value), body);
        }
    }

    internal static IEnumerable<string> Tokenize(string text) =>
        TokenPattern.Matches(text.ToLowerInvariant()).Select(m => m.Value);

    /// <summary>The ~600-character window of the page that contains the most question terms.</summary>
    private static string Excerpt(string text, IReadOnlyList<string> terms)
    {
        const int window = 600;
        var flat = Regex.Replace(text, @"\s+", " ").Trim();
        if (flat.Length <= window) return flat;
        var lower = flat.ToLowerInvariant();
        var bestStart = 0;
        var bestHits = -1;
        for (var start = 0; start < flat.Length; start += 100)
        {
            var end = Math.Min(flat.Length, start + window);
            var slice = lower[start..end];
            var hits = terms.Sum(t => CountOccurrences(slice, t));
            if (hits > bestHits)
            {
                bestHits = hits;
                bestStart = start;
            }
            if (end == flat.Length) break;
        }
        var excerpt = flat.Substring(bestStart, Math.Min(window, flat.Length - bestStart));
        return (bestStart > 0 ? "…" : "") + excerpt + (bestStart + window < flat.Length ? "…" : "");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private sealed record PageEntry(string Document, int Number, string Text, Dictionary<string, int> TermCounts, int Length)
    {
        public static PageEntry Create(string document, int number, string text)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var length = 0;
            foreach (var token in Tokenize(text))
            {
                length++;
                if (StopWords.Contains(token)) continue;
                counts[token] = counts.GetValueOrDefault(token) + 1;
            }
            return new PageEntry(document, number, text, counts, Math.Max(1, length));
        }
    }

    /// <summary>Default library folder: JARVIS_LIBRARY_DIR, else "JARVIS-library" next to the workspace, else LocalAppData.</summary>
    public static string DefaultRoot(string workspaceRoot)
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_LIBRARY_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        var parent = Directory.GetParent(Path.GetFullPath(workspaceRoot))?.FullName;
        if (parent is not null)
        {
            var sibling = Path.Combine(parent, "JARVIS-library");
            if (Directory.Exists(sibling)) return sibling;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "Library");
    }

    public static string FormatHits(IReadOnlyList<LibraryHit> hits)
    {
        var text = new StringBuilder();
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            text.AppendLine($"[{i + 1}] {hit.Document}, page {hit.Page} (relevance {hit.Score}):");
            text.AppendLine("    " + hit.Excerpt);
        }
        return text.ToString().TrimEnd();
    }
}
