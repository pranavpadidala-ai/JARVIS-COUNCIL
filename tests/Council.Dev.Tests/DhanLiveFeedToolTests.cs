using System.Buffers.Binary;
using Council.App.Finance;

namespace Council.Dev.Tests;

public sealed class DhanLiveFeedToolTests
{
    [Fact]
    public void ParsesTickerPacketUsingLittleEndianFields()
    {
        var packet = new byte[17];
        packet[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1, 2), 17);
        packet[3] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), 1333);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(9, 4), BitConverter.SingleToInt32Bits(1523.45f));
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(13, 4), 1_762_000_000);

        var tick = Assert.Single(DhanFeedPacketParser.Parse(packet));

        Assert.Equal("NSE_EQ", tick.ExchangeSegment);
        Assert.Equal(1333, tick.SecurityId);
        Assert.Equal(1523.45, tick.LastPrice!.Value, 1);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_762_000_000), tick.LastTradeTimeUtc!.Value);
    }

    [Fact]
    public void ParsesQuoteAndPreviousClosePackets()
    {
        var quote = new byte[51];
        quote[0] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(quote.AsSpan(1, 2), 51);
        quote[3] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(quote.AsSpan(4, 4), 500325);
        BinaryPrimitives.WriteInt32LittleEndian(quote.AsSpan(9, 4), BitConverter.SingleToInt32Bits(2500f));
        BinaryPrimitives.WriteInt32LittleEndian(quote.AsSpan(15, 4), 1_762_000_000);
        BinaryPrimitives.WriteInt32LittleEndian(quote.AsSpan(23, 4), 123456);

        var previousClose = new byte[17];
        previousClose[0] = 6;
        BinaryPrimitives.WriteUInt16LittleEndian(previousClose.AsSpan(1, 2), 17);
        previousClose[3] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(previousClose.AsSpan(4, 4), 500325);
        BinaryPrimitives.WriteInt32LittleEndian(previousClose.AsSpan(9, 4), BitConverter.SingleToInt32Bits(2475f));

        var parsed = DhanFeedPacketParser.Parse(quote.Concat(previousClose).ToArray());

        Assert.Equal(2, parsed.Count);
        Assert.Equal("BSE_EQ", parsed[0].ExchangeSegment);
        Assert.Equal(2500d, parsed[0].LastPrice!.Value);
        Assert.Equal(123456L, parsed[0].Volume!.Value);
        Assert.Equal(2475d, parsed[1].PreviousClose!.Value);
    }

    [Fact]
    public void ReadsServerDisconnectReasonCode()
    {
        var packet = new byte[10];
        packet[0] = 50;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1, 2), 10);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(8, 2), 807);

        Assert.Equal(807, DhanFeedPacketParser.TryReadDisconnectCode(packet)!.Value);
    }

    [Fact]
    public void IgnoresUnknownSegmentsAndMalformedPackets()
    {
        var packet = new byte[17];
        packet[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1, 2), 17);
        packet[3] = 99;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), 1333);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(9, 4), BitConverter.SingleToInt32Bits(100f));

        Assert.Empty(DhanFeedPacketParser.Parse(packet));
        Assert.Empty(DhanFeedPacketParser.Parse(packet.AsSpan(0, 9)));
    }

    [Fact]
    public async Task StartRequiresCredentialsAndDoesNotAcceptTradingArguments()
    {
        var tool = new DhanLiveFeedTool(() => null, () => null);

        var noCredentials = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "start",
            ["instruments"] = "NSE_EQ:1333"
        });
        var orderArgument = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "start",
            ["instruments"] = "NSE_EQ:1333",
            ["order"] = "BUY"
        });

        Assert.False(noCredentials.Success);
        Assert.Contains("not connected", noCredentials.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(orderArgument.Success);
    }

    [Fact]
    public async Task LivePriceAlertsPersistAcrossInstancesAndCanBeRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDhanAlerts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = Path.Combine(root, "alerts.json");
        try
        {
            var first = new DhanLiveFeedTool(() => null, () => null, store, _ => { });
            var saved = await first.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_alert",
                ["instrument"] = "NSE_EQ:1333",
                ["condition"] = "above",
                ["price"] = "1525.50",
                ["label"] = "RELIANCE"
            });

            Assert.True(saved.Success, saved.Message);
            var id = saved.Message.Split("ID=", StringSplitOptions.None)[1].Split(':', ' ')[0];
            Assert.Equal(8, id.Length);

            var second = new DhanLiveFeedTool(() => null, () => null, store, _ => { });
            var listed = await second.ExecuteAsync(new Dictionary<string, string> { ["action"] = "alerts" });
            Assert.True(listed.Success, listed.Message);
            Assert.Contains("RELIANCE", listed.Message);
            Assert.Contains("ACTIVE", listed.Message);
            Assert.Contains("1,525.50", listed.Message);

            var removed = await second.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "remove_alert",
                ["alert_id"] = id
            });
            Assert.True(removed.Success, removed.Message);
            var empty = await second.ExecuteAsync(new Dictionary<string, string> { ["action"] = "alerts" });
            Assert.Contains("No Dhan live price alerts", empty.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LivePriceAlertFreshnessRejectsStaleOrMissingTradeTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var fresh = new DhanFeedTick("NSE_EQ", 1333, 1500, now.AddSeconds(-30), null, null, now);
        var stale = fresh with { LastTradeTimeUtc = now.AddMinutes(-16) };
        var missing = fresh with { LastTradeTimeUtc = null };
        var future = fresh with { LastTradeTimeUtc = now.AddMinutes(2) };

        Assert.True(DhanLiveFeedTool.IsFreshTick(fresh));
        Assert.False(DhanLiveFeedTool.IsFreshTick(stale));
        Assert.False(DhanLiveFeedTool.IsFreshTick(missing));
        Assert.False(DhanLiveFeedTool.IsFreshTick(future));
    }

    [Fact]
    public async Task LivePriceAlertsRejectMalformedPersistentStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDhanAlerts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = Path.Combine(root, "alerts.json");
        try
        {
            await File.WriteAllTextAsync(store, "[null]");
            var tool = new DhanLiveFeedTool(() => null, () => null, store, _ => { });
            var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "alerts" });

            Assert.False(result.Success);
            Assert.Contains("invalid or duplicate alert entries", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LivePriceAlertEvaluatorUsesInclusiveThresholdsAndHonoursOneShotState()
    {
        var above = new DhanLivePriceAlert("A1", "NSE_EQ", 1333, "RELIANCE", "above", 1500m, true);
        var below = new DhanLivePriceAlert("B1", "NSE_EQ", 1333, "RELIANCE", "below", 1500m, true);
        var disabled = above with { Enabled = false };

        Assert.False(DhanLiveFeedTool.IsAlertTriggered(above, 1499.99m));
        Assert.True(DhanLiveFeedTool.IsAlertTriggered(above, 1500m));
        Assert.False(DhanLiveFeedTool.IsAlertTriggered(below, 1500.01m));
        Assert.True(DhanLiveFeedTool.IsAlertTriggered(below, 1500m));
        Assert.False(DhanLiveFeedTool.IsAlertTriggered(disabled, 1600m));
    }

    [Fact]
    public async Task LivePriceAlertsRejectInvalidInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDhanAlerts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tool = new DhanLiveFeedTool(() => null, () => null, Path.Combine(root, "alerts.json"), _ => { });
            var badInstrument = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_alert", ["instrument"] = "NSE:1333", ["condition"] = "above", ["price"] = "1500"
            });
            var badCondition = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_alert", ["instrument"] = "NSE_EQ:1333", ["condition"] = "crosses", ["price"] = "1500"
            });
            var badPrice = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "set_alert", ["instrument"] = "NSE_EQ:1333", ["condition"] = "below", ["price"] = "-1"
            });

            Assert.False(badInstrument.Success);
            Assert.False(badCondition.Success);
            Assert.False(badPrice.Success);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsInvalidSubscriptionsWithoutConnecting()
    {
        var tool = new DhanLiveFeedTool();

        var badSegment = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "start",
            ["instruments"] = "EVIL:1333"
        });
        var badMode = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "start",
            ["instruments"] = "NSE_EQ:1333",
            ["mode"] = "full"
        });
        var tooMany = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "start",
            ["instruments"] = string.Join(",", Enumerable.Range(1, 101).Select(id => $"NSE_EQ:{id}"))
        });

        Assert.False(badSegment.Success);
        Assert.False(badMode.Success);
        Assert.False(tooMany.Success);
    }
}
