using System.Globalization;
using System.Net;
using System.Text;
using Council.App.Finance;

namespace Council.Dev.Tests;

public sealed class IndianMarketToolTests
{
    [Fact]
    public async Task EncryptsLocalHoldingsWithCurrentWindowsUserDpapi()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "JarvisMarketTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "watchlist.json");
        try
        {
            var tool = new IndianMarketTool(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException())), path);
            var saved = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_holding", ["symbol"] = "RELIANCE", ["quantity"] = "10", ["average_buy_price"] = "100"
            });
            Assert.True(saved.Success, saved.Message);
            var stored = await File.ReadAllTextAsync(path);
            Assert.StartsWith("dpapi:v1:", stored);
            Assert.DoesNotContain("RELIANCE", stored);
            var listed = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "list" });
            Assert.True(listed.Success, listed.Message);
            Assert.Contains("RELIANCE", listed.Message);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ReadsLegacyPlaintextWatchlistAndEncryptsItOnNextWrite()
    {
        var folder = Path.Combine(Path.GetTempPath(), "JarvisMarketTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "watchlist.json");
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(path, """{"holdings":[{"symbol":"TCS","exchange":"NSE","quantity":5,"averageBuyPrice":2500}],"watchlist":[],"alerts":[]}""");
            var tool = new IndianMarketTool(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException())), path);
            var listed = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "list" });
            Assert.True(listed.Success, listed.Message);
            Assert.Contains("TCS", listed.Message);
            var updated = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_holding", ["symbol"] = "TCS", ["quantity"] = "6", ["average_buy_price"] = "2550"
            });
            Assert.True(updated.Success, updated.Message);
            if (OperatingSystem.IsWindows())
            {
                var stored = await File.ReadAllTextAsync(path);
                Assert.StartsWith("dpapi:v1:", stored);
            }
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task SavesHoldingAndCalculatesPortfolioValueFromEodQuote()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var json = $$"""
        {
          "data": [
            { "date": "{{today.AddDays(-1):yyyy-MM-dd}}", "close": 105.0, "prev_close": 102.0, "volume": 1200 },
            { "date": "{{today:yyyy-MM-dd}}", "close": 110.0, "prev_close": 105.0, "volume": 1500 }
          ],
          "meta": { "exchange": "NSE", "symbol": "RELIANCE", "count": 2 }
        }
        """;
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(handler);
        var folder = Path.Combine(Path.GetTempPath(), "JarvisMarketTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "watchlist.json");
        try
        {
            var tool = new IndianMarketTool(client, path);
            var saved = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_holding",
                ["symbol"] = "RELIANCE",
                ["quantity"] = "10",
                ["average_buy_price"] = "100"
            });
            Assert.True(saved.Success, saved.Message);

            var portfolio = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "portfolio" });
            Assert.True(portfolio.Success, portfolio.Message);
            Assert.Contains("RELIANCE (NSE)", portfolio.Message);
            Assert.Contains("EOD close", portfolio.Message);
            Assert.Contains("Unrealized P&L", portfolio.Message);
            Assert.Contains("100.00", portfolio.Message);
            Assert.Contains("1,100.00", portfolio.Message);
            Assert.Contains("not a live/intraday feed", portfolio.Message);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task RejectsInvalidSymbolWithoutCallingMarketDataService()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        using var client = new HttpClient(handler);
        var tool = new IndianMarketTool(client, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "watchlist.json"));
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "quote",
            ["symbol"] = "../.env"
        });
        Assert.False(result.Success);
        Assert.Contains("valid NSE/BSE trading symbol", result.Message);
    }

    [Fact]
    public async Task AddsAndRemovesWatchlistSymbolsLocally()
    {
        var folder = Path.Combine(Path.GetTempPath(), "JarvisMarketTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "watchlist.json");
        try
        {
            var tool = new IndianMarketTool(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException())), path);
            var added = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "add_watch", ["symbol"] = "TCS"
            });
            Assert.True(added.Success, added.Message);
            var listed = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "list" });
            Assert.Contains("TCS (NSE)", listed.Message);
            Assert.Contains("separate from broker holdings", listed.Message);
            Assert.Contains("dhan_readonly", listed.Message);
            Assert.DoesNotContain("No broker account is connected", listed.Message);
            var removed = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "remove", ["symbol"] = "TCS"
            });
            Assert.True(removed.Success, removed.Message);
            var after = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "list" });
            Assert.DoesNotContain("TCS (NSE)", after.Message);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }


    [Fact]
    public async Task EodPriceAlertTriggersOnceAndCanBeListed()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var json = $$"""
        {
          "data": [
            { "date": "{{today:yyyy-MM-dd}}", "close": 110.0, "prev_close": 105.0, "volume": 1500 }
          ]
        }
        """;
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(handler);
        var folder = Path.Combine(Path.GetTempPath(), "JarvisMarketTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "watchlist.json");
        try
        {
            var tool = new IndianMarketTool(client, path);
            var saved = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_alert", ["symbol"] = "RELIANCE", ["condition"] = "above", ["price"] = "108"
            });
            Assert.True(saved.Success, saved.Message);
            var check = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "check_alerts" });
            Assert.True(check.Success, check.Message);
            Assert.Contains("TRIGGERED", check.Message);
            Assert.Contains("₹110.00", check.Message);
            var secondCheck = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "check_alerts" });
            Assert.Contains("No active EOD price alerts", secondCheck.Message);
            var listed = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "alerts" });
            Assert.Contains("TRIGGERED", listed.Message);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void DailyMonitorSchedulesAtNineIstAndSkipsWeekends()
    {
        var offset = TimeSpan.FromMinutes(330);
        var beforeOpen = new DateTimeOffset(2026, 11, 13, 8, 0, 0, offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 9, 0, 0, offset),
            IndianMarketMonitorService.GetNextScheduledRun(beforeOpen, new TimeOnly(9, 0)));
        var afterOpen = new DateTimeOffset(2026, 11, 13, 10, 0, 0, offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 9, 0, 0, offset),
            IndianMarketMonitorService.GetNextScheduledRun(afterOpen, new TimeOnly(9, 0)));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
