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
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_762_000_000), tick.LastTradeTimeUtc);
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
