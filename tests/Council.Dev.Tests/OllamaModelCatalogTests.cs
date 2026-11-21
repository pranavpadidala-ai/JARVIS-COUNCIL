using System.Net;
using System.Text;
using Council.App.Chat;

namespace Council.Dev.Tests;

public sealed class OllamaModelCatalogTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    [Fact]
    public async Task ListsInstalledModelNamesFromTagsEndpoint()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"models":[{"name":"qwen3.5:2b-q4_K_M"},{"name":"glm-ocr:latest"},{"name":"qwen3-vl:2b"}]}""");
        using var http = new HttpClient(handler);

        var models = await OllamaModelCatalog.ListInstalledAsync("http://localhost:11434", http);

        Assert.Equal(new[] { "glm-ocr:latest", "qwen3-vl:2b", "qwen3.5:2b-q4_K_M" }, models);
        Assert.Equal("http://localhost:11434/api/tags", handler.LastUri?.ToString());
    }

    [Fact]
    public async Task ReturnsEmptyWhenOllamaIsNotRunning()
    {
        using var http = new HttpClient(new FailingHandler());

        var models = await OllamaModelCatalog.ListInstalledAsync("http://localhost:11434", http);

        Assert.Empty(models);
    }

    [Fact]
    public async Task ReturnsEmptyForUnexpectedResponse()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "not json"));

        Assert.Empty(await OllamaModelCatalog.ListInstalledAsync("http://localhost:11434", http));
    }

    [Fact]
    public void ChatModelsAreListedBeforeVisionAndOcrModels()
    {
        var ordered = OllamaModelCatalog.OrderForChat(new[] { "glm-ocr:latest", "qwen3-vl:2b", "qwen3.5:2b-q4_K_M" });

        Assert.Equal("qwen3.5:2b-q4_K_M", ordered[0]);
    }
}
