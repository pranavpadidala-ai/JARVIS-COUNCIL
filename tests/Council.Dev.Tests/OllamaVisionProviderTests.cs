using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Council.AI.Providers;
using Council.Core.Models;

namespace Council.Dev.Tests;

public sealed class OllamaVisionProviderTests
{
    [Fact]
    public async Task DescribeImagesAsync_SendsImageBytesToLocalOllamaAndReturnsText()
    {
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/chat", request.RequestUri?.AbsolutePath);
            using var body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            Assert.Equal("moondream", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.Equal(2048, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
            var images = root.GetProperty("messages")[0].GetProperty("images");
            Assert.Single(images.EnumerateArray());
            Assert.Equal(Convert.ToBase64String([1, 2, 3]), images[0].GetString());
            Assert.Contains("PDF page 2",
                root.GetProperty("messages")[0].GetProperty("content").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { message = new { content = "A load-displacement plot." } })
            };
        });

        var provider = new OllamaVisionProvider(httpClient: new HttpClient(handler));
        var result = await provider.DescribeImagesAsync(
            "Describe this plot.",
            [new AIImage("PDF page 2, image 1", "image/png", [1, 2, 3])]);

        Assert.Equal("A load-displacement plot.", result);
    }

    [Fact]
    public async Task DescribeImagesAsync_RejectsUnsupportedImageMimeType()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Request should not be sent."));
        var provider = new OllamaVisionProvider(httpClient: new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.DescribeImagesAsync(
                "Describe.",
                [new AIImage("bad", "image/bmp", [1, 2, 3])]));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
