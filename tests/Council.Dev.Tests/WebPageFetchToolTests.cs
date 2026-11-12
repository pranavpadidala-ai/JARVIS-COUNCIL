using System.Net;
using Council.App;

namespace Council.Dev.Tests;

public sealed class WebPageFetchToolTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.20.1.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.100.1.1", false)]
    [InlineData("169.254.10.20", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::192.168.1.1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("2606:4700:4700::1111", true)]
    public void PublicAddressFilterBlocksPrivateAndReservedRanges(string value, bool expected)
    {
        Assert.Equal(expected, WebPageFetchTool.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public void ExtractReadableTextRemovesScriptsStylesAndMarkup()
    {
        const string html = """
            <html><head><title>Sample</title><style>.hidden{display:none}</style>
            <script>ignoreThisSecret()</script></head>
            <body><nav>Menu</nav><main><h1>Research &amp; validation</h1>
            <p>Aluminium mullions resist wind loading.</p></main></body></html>
            """;

        var text = WebPageFetchTool.ExtractReadableText(html);

        Assert.Contains("Research & validation", text);
        Assert.Contains("Aluminium mullions resist wind loading.", text);
        Assert.DoesNotContain("ignoreThisSecret", text);
        Assert.DoesNotContain("display:none", text);
        Assert.DoesNotContain("Menu", text);
    }

    [Fact]
    public async Task ExecuteBlocksLoopbackAndNonStandardPortsBeforeNetworkAccess()
    {
        var tool = new WebPageFetchTool();
        var loopback = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["url"] = "http://127.0.0.1/"
        });
        var port = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["url"] = "https://example.com:8443/"
        });

        Assert.False(loopback.Success);
        Assert.Contains("Private or reserved", loopback.Message);
        Assert.False(port.Success);
        Assert.Contains("ports 80 and 443", port.Message);
    }
}
