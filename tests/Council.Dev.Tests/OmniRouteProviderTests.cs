using System.Net;
using System.Text;
using Council.AI.Providers;
using Council.Core.Models;

namespace Council.Dev.Tests;

public sealed class OmniRouteProviderTests
{
    [Fact]
    public async Task AskAsync_SendsOpenAiCompatibleRequestToGateway()
    {
        string? body = null;
        string? authorization = null;
        var handler = new StubHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization?.ToString();
            Assert.EndsWith("/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"model\":\"free-model/model-a\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Hello from OmniRoute\"}}]}",
                    Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var provider = new OmniRouteProvider(
            "local-gateway-key",
            "free-model/model-a",
            "http://localhost:20128/v1",
            "free-model",
            client);

        var result = await provider.AskAsync(new AIRequest(
        [
            new AIMessage("system", "You are JARVIS."),
            new AIMessage("user", "Hello")
        ]));

        Assert.Equal("Hello from OmniRoute", result.Content);
        Assert.Equal("OmniRoute (allowlisted models)", result.Provider);
        Assert.Equal("Bearer local-gateway-key", authorization);
        Assert.Contains("\"model\":\"free-model/model-a\"", body);
        Assert.Contains("\"stream\":false", body);
    }

    [Fact]
    public void Constructor_RejectsAutoModelToPreventUnboundedRouting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new OmniRouteProvider(
                "key", "auto", "http://localhost:20128/v1", "free-model",
                new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))));

        Assert.Contains("explicit provider/model", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsModelOutsideConfiguredFreePrefixes()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new OmniRouteProvider(
                "key", "paid-provider/model-a", "http://localhost:20128/v1", "free-model",
                new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))));

        Assert.Contains("not in OMNIROUTE_FREE_MODEL_PREFIXES", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsRemoteHttpGateway()
    {
        Assert.Throws<ArgumentException>(() =>
            new OmniRouteProvider(
                "key", "free-model/model-a", "http://example.com/v1", "free-model"));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
