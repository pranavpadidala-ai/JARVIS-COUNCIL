using System.Net;
using System.Text;
using Council.AI.Providers;
using Council.Core.Models;

namespace Council.Dev.Tests;

public sealed class OllamaProviderTests
{
    [Fact]
    public async Task AskAsync_SendsChatRequestToLocalOllamaAndReadsResponse()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"model\":\"qwen3.5:2b-q4_K_M\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello from local AI\"},\"done\":true}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var provider = new OllamaProvider(
            "qwen3.5:2b-q4_K_M",
            "http://localhost:11434",
            contextTokens: 4096,
            httpClient: client);

        var result = await provider.AskAsync(new AIRequest(
            [new AIMessage("user", "Hello")], JsonMode: true));

        Assert.Equal("Ollama (local)", result.Provider);
        Assert.Equal("Hello from local AI", result.Content);
        Assert.Equal("qwen3.5:2b-q4_K_M", result.Model);
        Assert.Contains("\"num_ctx\":4096", requestBody);
        Assert.Contains("\"stream\":false", requestBody);
        Assert.Contains("\"format\":\"json\"", requestBody);
    }

    [Fact]
    public async Task AskAsync_ProvidesHelpfulErrorWhenLocalServerIsUnavailable()
    {
        var handler = new StubHandler(_ =>
            throw new HttpRequestException("Connection refused"));

        using var client = new HttpClient(handler);
        var provider = new OllamaProvider(httpClient: client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.AskAsync(new AIRequest([new AIMessage("user", "Hello")])));

        Assert.Contains("Cannot connect to Ollama", exception.Message);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
