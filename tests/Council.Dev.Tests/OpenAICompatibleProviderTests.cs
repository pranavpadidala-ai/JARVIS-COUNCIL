using System.Net;
using System.Text;
using Council.AI.Providers;
using Council.Core.Models;

namespace Council.Dev.Tests;

public sealed class OpenAICompatibleProviderTests
{
    [Fact]
    public async Task Sends_chat_completion_request_and_parses_response()
    {
        var handler = new RecordingHandler(
            """{"choices":[{"message":{"content":"Gateway response"}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider(
            "https://gateway.example/v1/",
            "test-token",
            "model-test",
            "Test gateway",
            httpClient);

        var response = await provider.AskAsync(
            new AIRequest([new AIMessage("user", "Hello")], JsonMode: true));

        Assert.Equal("Gateway response", response.Content);
        Assert.Equal("model-test", response.Model);
        Assert.Equal("https://gateway.example/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-token", handler.AuthorizationParameter);
        Assert.Contains("\"response_format\":{\"type\":\"json_object\"}", handler.RequestBody);
    }

    [Fact]
    public async Task Includes_gateway_error_when_request_fails()
    {
        var handler = new RecordingHandler(
            """{"error":{"message":"quota exceeded"}}""",
            HttpStatusCode.TooManyRequests);
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider(
            "https://gateway.example/v1",
            "test-token",
            "model-test",
            httpClient: httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.AskAsync(
                new AIRequest([new AIMessage("user", "Hello")])));

        Assert.Contains("quota exceeded", exception.Message);
    }

    private sealed class RecordingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
