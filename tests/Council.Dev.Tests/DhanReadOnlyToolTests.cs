using System.Net;
using System.Text;
using Council.App.Finance;

namespace Council.Dev.Tests;

public sealed class DhanReadOnlyToolTests
{
    [Fact]
    public async Task MissingCredentialsFailWithoutMakingNetworkRequest()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var client = new HttpClient(handler);
        var tool = new DhanReadOnlyTool(client, () => null, () => null);

        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "profile" });

        Assert.False(result.Success);
        Assert.Contains("not connected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuoteUsesReadOnlyLtpEndpointAndFormatsSnapshot()
    {
        HttpMethod? capturedMethod = null;
        string? capturedUrl = null;
        string? capturedToken = null;
        string? capturedClientId = null;
        string? capturedBody = null;
        var handler = new StubHandler(async (request, _) =>
        {
            capturedMethod = request.Method;
            capturedUrl = request.RequestUri!.ToString();
            capturedToken = request.Headers.GetValues("access-token").Single();
            capturedClientId = request.Headers.GetValues("client-id").Single();
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"status":"success","data":{"NSE_EQ":{"1333":{"last_price":1523.45}}}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var client = new HttpClient(handler);
        var tool = new DhanReadOnlyTool(client, () => "1000000001", () => "test-token");

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "quote",
            ["exchange_segment"] = "NSE_EQ",
            ["security_id"] = "1333"
        });

        Assert.True(result.Success);
        Assert.Contains("₹1,523.45", result.Message);
        Assert.Contains("snapshot", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("https://api.dhan.co/v2/marketfeed/ltp", capturedUrl);
        Assert.Equal("test-token", capturedToken);
        Assert.Equal("1000000001", capturedClientId);
        Assert.Contains("\"NSE_EQ\":[1333]", capturedBody);
    }

    [Fact]
    public async Task InvalidSegmentAndExtraArgumentsAreRejected()
    {
        var tool = new DhanReadOnlyTool(
            new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."))),
            () => "1000000001",
            () => "test-token");

        var invalidSegment = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "quote",
            ["exchange_segment"] = "EVIL",
            ["security_id"] = "1333"
        });
        var extraArgument = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "profile",
            ["order"] = "BUY"
        });

        Assert.False(invalidSegment.Success);
        Assert.Contains("exchange_segment", invalidSegment.Message);
        Assert.False(extraArgument.Success);
        Assert.Contains("Unexpected argument", extraArgument.Message);
    }

    [Fact]
    public async Task HoldingsReturnOnlyPortfolioFieldsAndNeverExposeCredentials()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.dhan.co/v2/holdings", request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"tradingSymbol":"HDFC","exchange":"ALL","isin":"INE001A01036","totalQty":10,"avgCostPrice":2655.0,"clientName":"Private Name"}]""",
                    Encoding.UTF8,
                    "application/json")
            });
        });
        using var client = new HttpClient(handler);
        var tool = new DhanReadOnlyTool(client, () => "1000000001", () => "secret-token");

        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "holdings" });

        Assert.True(result.Success);
        Assert.Contains("HDFC", result.Message);
        Assert.Contains("₹2,655.00", result.Message);
        Assert.DoesNotContain("Private Name", result.Message);
        Assert.DoesNotContain("secret-token", result.Message);
    }

    [Fact]
    public async Task ExpiredTokenProducesActionableMessageWithoutEchoingResponseBody()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("secret-token invalid")
        }));
        using var client = new HttpClient(handler);
        var tool = new DhanReadOnlyTool(client, () => "1000000001", () => "secret-token");

        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "profile" });

        Assert.False(result.Success);
        Assert.Contains("expired", result.Message);
        Assert.DoesNotContain("secret-token", result.Message);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
