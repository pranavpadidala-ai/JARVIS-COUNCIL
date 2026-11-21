using System.Text;
using System.Text.RegularExpressions;
using Council.Memory;

namespace Council.App.Knowledge;

public sealed record LibraryHit(string Document, int Page, double Score, string Excerpt);

/// <summary>
/// Search over the owner's private library of standards and books. Each document is a .txt file
/// with "=== PAGE n ===" markers, made from the owner's own PDFs and kept outside the repository.
/// Ranking reuses JARVIS's existing offline <see cref="MemoryRecallEngine"/> (BM25 with stemming and
/// phrase bonuses); this class only loads pages, caches them until files change, and builds excerpts.
/// </summary>
public sealed class StandardsLibrary
{
    private static readonly Regex PageMarker = new(@"^=== PAGE (\d+) ===\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Word = new(@"[a-z0-9]{2,}", RegexOptions.CultureInvariant);

    private readonly string _root;
    private readonly object _gate = new();
    private List<PageEntry> _pages = new();
    private string _signature = "";

    public StandardsLibrary(string root) => _root = root;

    public string Root => _root;

    public IReadOnlyList<(string Document, int Pages)> Documents() =>
        LoadPages().GroupBy(p => p.Document).Select(g => (g.Key, g.Count())).OrderBy(d => d.Key).ToList();

    public IReadOnlyList<LibraryHit> Search(string query, int maxResults = 5, string? documentFilter = null)
    {
        var pages = LoadPages();
        if (!string.IsNullOrWhiteSpace(documentFilter))
            pages = pages.Where(p => p.Document.Contains(documentFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        var terms = Word.Matches(query.ToLowerInvariant()).Select(m => m.Value).Distinct().ToList();
        return MemoryRecallEngine.Rank(query, pages, p => p.Text, maxResults: Math.Clamp(maxResults, 1, 10))
            .Select(hit => new LibraryHit(hit.Item.Document, hit.Item.Number, Math.Round(hit.Score, 2), Excerpt(hit.Item.Text, terms)))
            .ToList();
    }

    private List<PageEntry> LoadPages()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_root))
            {
                _pages = new();
                _signature = "";
                return _pages;
            }

            var files = Directory.EnumerateFiles(_root, "*.txt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var signature = string.Join("|", files.Select(f => f + ":" + new FileInfo(f).Length + ":" + File.GetLastWriteTimeUtc(f).Ticks));
            if (signature != _signature)
            {
                _pages = files.SelectMany(f => ReadPages(f, Path.GetFileNameWithoutExtension(f))).ToList();
                _signature = signature;
            }
            return _pages;
        }
    }

    private static IEnumerable<PageEntry> ReadPages(string file, string document)
    {
        var text = File.ReadAllText(file);
        var markers = PageMarker.Matches(text);
        if (markers.Count == 0)
        {
            yield return new PageEntry(document, 1, text);
            yield break;
        }
        for (var i = 0; i < markers.Count; i++)
        {
            var start = markers[i].Index + markers[i].Length;
            var end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
            var body = text[start..end];
            if (body.Trim().Length < 40) continue; // blank or watermark-only page
            yield return new PageEntry(document, int.Parse(markers[i].Groups[1].Value), body);
        }
    }

    /// <summary>The ~600-character window of the page that contains the most question words.</summary>
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
            var hits = terms.Sum(t => Regex.Matches(slice, Regex.Escape(t)).Count);
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

    private sealed record PageEntry(string Document, int Number, string Text);

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
