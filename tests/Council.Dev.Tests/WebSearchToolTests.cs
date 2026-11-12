using Council.App;

namespace Council.Dev.Tests;

public sealed class WebSearchToolTests
{
    [Fact]
    public void ParseResultsExtractsTitlesSnippetsAndRedirectedUrls()
    {
        const string html = """
            <div class="result">
              <a rel="nofollow" class="result__a" href="https://example.com/engineering"><span>Structural &amp; facade design</span></a>
              <a class="result__snippet" href="https://example.com/engineering">A <b>technical</b> overview of aluminium facade design.</a>
            </div>
            <div class="result">
              <a class="result__a" href="https://duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.org%2Fpaper%3Fid%3D12&amp;rut=abc"><span>Research paper</span></a>
              <a class="result__snippet" href="https://example.org/paper">Experimental results and validation.</a>
            </div>
            """;

        var results = WebSearchTool.ParseResults(html, 5);

        Assert.Equal(2, results.Count);
        Assert.Equal("Structural & facade design", results[0].Title);
        Assert.Equal("https://example.com/engineering", results[0].Url);
        Assert.Contains("technical overview", results[0].Snippet);
        Assert.Equal("https://example.org/paper?id=12", results[1].Url);
    }

    [Fact]
    public void ParseResultsRejectsNonHttpSchemesAndHonoursResultLimit()
    {
        const string html = """
            <a class="result__a" href="javascript:alert(1)">Unsafe</a>
            <a class="result__a" href="https://example.com/one">One</a>
            <a class="result__a" href="https://example.com/two">Two</a>
            """;

        var results = WebSearchTool.ParseResults(html, 1);

        var result = Assert.Single(results);
        Assert.Equal("One", result.Title);
        Assert.Equal("https://example.com/one", result.Url);
    }

    [Fact]
    public async Task ExecuteRejectsInvalidQueryWithoutNetworkAccess()
    {
        var tool = new WebSearchTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = " "
        });

        Assert.False(result.Success);
        Assert.Contains("2 to 300 characters", result.Message);
    }
}
