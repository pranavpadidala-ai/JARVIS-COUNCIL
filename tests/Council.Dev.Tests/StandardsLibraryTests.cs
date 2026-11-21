using Council.App.Knowledge;
using Xunit;

namespace Council.Dev.Tests;

public sealed class StandardsLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jarvis-library-" + Guid.NewGuid().ToString("N"));

    public StandardsLibraryTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "EN 1999-1-1.txt"),
            "\n=== PAGE 1 ===\nForeword and scope of Eurocode 9 for aluminium structures, general rules and rules for buildings.\n" +
            "\n=== PAGE 2 ===\n@watermark\n" +
            "\n=== PAGE 40 ===\n6.1.3 Partial safety factors. The partial factor gamma M1 for resistance of cross-sections is 1.10 and gamma M2 for net section fracture is 1.25.\n" +
            "\n=== PAGE 55 ===\nHeat affected zone (HAZ) softening adjacent to welds reduces the 0.2% proof strength; rho o,haz for 6082 T6 is given in Table 3.2b.\n");
        File.WriteAllText(Path.Combine(_root, "Dwight book.txt"),
            "\n=== PAGE 12 ===\nAluminium has a low elastic modulus, about one third of steel, so buckling and deflection often govern design.\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindsTheMostRelevantPageWithItsDocumentAndPageNumber()
    {
        var hits = new StandardsLibrary(_root).Search("HAZ softening near welds 6082");
        Assert.NotEmpty(hits);
        Assert.Equal("EN 1999-1-1", hits[0].Document);
        Assert.Equal(55, hits[0].Page);
        Assert.Contains("Heat affected zone", hits[0].Excerpt);
    }

    [Fact]
    public void MatchesClauseNumbersAndCanFilterByDocument()
    {
        var library = new StandardsLibrary(_root);
        Assert.Equal(40, library.Search("6.1.3 partial factor")[0].Page);
        Assert.All(library.Search("aluminium", document: "dwight"), hit => Assert.Equal("Dwight book", hit.Document));
    }

    [Fact]
    public void SkipsWatermarkOnlyPagesAndListsDocuments()
    {
        var docs = new StandardsLibrary(_root).Documents();
        Assert.Contains(docs, d => d.Document == "EN 1999-1-1" && d.Pages == 3);
        Assert.Contains(docs, d => d.Document == "Dwight book" && d.Pages == 1);
    }

    [Fact]
    public void PicksUpNewFilesWithoutRestart()
    {
        var library = new StandardsLibrary(_root);
        Assert.Empty(library.Search("ponding"));
        File.WriteAllText(Path.Combine(_root, "ADM.txt"), "\n=== PAGE 9 ===\nPonding of flat roofs must be checked for aluminium roof members and their deflection.\n");
        Assert.Equal("ADM", library.Search("ponding")[0].Document);
    }

    [Fact]
    public async Task ToolReportsEmptyLibraryAndQuestionsWithNoMatch()
    {
        var empty = new StandardsLibraryTool(new StandardsLibrary(Path.Combine(_root, "missing")));
        var list = await empty.ExecuteAsync(new Dictionary<string, string> { ["action"] = "list" });
        Assert.False(list.Success);

        var tool = new StandardsLibraryTool(new StandardsLibrary(_root));
        var none = await tool.ExecuteAsync(new Dictionary<string, string> { ["query"] = "xylophone" });
        Assert.True(none.Success);
        Assert.Contains("No matching passages", none.Message);

        var found = await tool.ExecuteAsync(new Dictionary<string, string> { ["query"] = "elastic modulus buckling" });
        Assert.Contains("Dwight book, page 12", found.Message);
    }
}
