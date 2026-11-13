using System.Net;
using System.Text;
using Council.App.Finance;

namespace Council.Dev.Tests;

public sealed class DhanInstrumentLookupToolTests
{
    private const string Csv =
        "SEM_EXM_EXCH_ID,SEM_SEGMENT,SEM_SMST_SECURITY_ID,SEM_INSTRUMENT_NAME,SEM_TRADING_SYMBOL,SEM_CUSTOM_SYMBOL,SM_SYMBOL_NAME,SEM_SERIES\n" +
        "NSE,E,2885,EQUITY,RELIANCE,RELIANCE INDUSTRIES,RELIANCE INDUSTRIES,L\n" +
        "BSE,E,500325,EQUITY,RELIANCE,RELIANCE INDUSTRIES,RELIANCE INDUSTRIES,A\n" +
        "NSE,D,999999,FUTSTK,RELIANCE-APR-FUT,RELIANCE APR FUT,RELIANCE,L\n" +
        "NSE,E,1333,EQUITY,HDFCBANK,HDFC BANK,HDFCBANK, EQ\n";

    [Fact]
    public async Task ResolvesExactNseEquitySymbolAndReturnsDhanSegment()
    {
        var handler = new StubHandler();
        using var client = new HttpClient(handler);
        var tool = new DhanInstrumentLookupTool(client);

        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["symbol"] = " reliance " });

        Assert.True(result.Success);
        Assert.Contains("Security ID: 2885", result.Message);
        Assert.Contains("Exchange segment: NSE_EQ", result.Message);
        Assert.DoesNotContain("999999", result.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SupportsBseAndDoesNotReturnNseSecurityId()
    {
        var tool = new DhanInstrumentLookupTool(new HttpClient(new StubHandler()));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["symbol"] = "RELIANCE",
            ["exchange"] = "BSE"
        });

        Assert.True(result.Success);
        Assert.Contains("Security ID: 500325", result.Message);
        Assert.Contains("Exchange segment: BSE_EQ", result.Message);
        Assert.DoesNotContain("2885", result.Message);
    }

    [Fact]
    public async Task ResolvesBatchOfSymbolsWithOneInstrumentMasterDownload()
    {
        var handler = new StubHandler();
        var tool = new DhanInstrumentLookupTool(new HttpClient(handler));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["symbols"] = "RELIANCE, HDFCBANK, RELIANCE",
            ["exchange"] = "NSE"
        });

        Assert.True(result.Success);
        Assert.Contains("Requested symbol: RELIANCE", result.Message);
        Assert.Contains("Security ID: 2885", result.Message);
        Assert.Contains("Requested symbol: HDFCBANK", result.Message);
        Assert.Contains("Security ID: 1333", result.Message);
        Assert.DoesNotContain("500325", result.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task BatchLookupReportsUnresolvedSymbolsAlongsideResolvedMatches()
    {
        var tool = new DhanInstrumentLookupTool(new HttpClient(new StubHandler()));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["symbols"] = "RELIANCE,UNKNOWN",
            ["exchange"] = "NSE"
        });

        Assert.True(result.Success);
        Assert.Contains("Security ID: 2885", result.Message);
        Assert.Contains("No exact NSE cash-equity match found for 'UNKNOWN'", result.Message);
    }

    [Fact]
    public async Task RejectsOversizedBatchAndMutuallyExclusiveSymbolArgumentsWithoutNetwork()
    {
        var handler = new StubHandler();
        var tool = new DhanInstrumentLookupTool(new HttpClient(handler));
        var tooMany = string.Join(',', Enumerable.Range(1, 31).Select(i => $"S{i}"));
        var oversized = await tool.ExecuteAsync(new Dictionary<string, string> { ["symbols"] = tooMany });
        var both = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["symbol"] = "TCS",
            ["symbols"] = "TCS,INFY"
        });

        Assert.False(oversized.Success);
        Assert.False(both.Success);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task RejectsUnsupportedExchangeAndExtraArgumentsWithoutNetwork()
    {
        var handler = new StubHandler();
        var tool = new DhanInstrumentLookupTool(new HttpClient(handler));

        var badExchange = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["symbol"] = "TCS",
            ["exchange"] = "MCX"
        });
        var extra = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["symbol"] = "TCS",
            ["order"] = "BUY"
        });

        Assert.False(badExchange.Success);
        Assert.False(extra.Success);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task MissingSymbolFailsWithoutNetwork()
    {
        var handler = new StubHandler();
        var tool = new DhanInstrumentLookupTool(new HttpClient(handler));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal("https://images.dhan.co/api-data/api-scrip-master.csv", request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Csv, Encoding.UTF8, "text/csv")
            });
        }
    }
}
