using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// Connects to OpenAI-compatible chat-completions endpoints such as OmniRoute,
/// OpenRouter, LM Studio and compatible local gateways.
/// </summary>
public sealed class OpenAICompatibleProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string _defaultModel;

    public string Name { get; }

    public OpenAICompatibleProvider(
        string baseUrl,
        string apiKey,
        string model = "auto",
        string name = "OpenAI-compatible",
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required.", nameof(model));

        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _endpoint = new Uri($"{normalizedBaseUrl}/chat/completions", UriKind.Absolute);
        _apiKey = apiKey;
        _defaultModel = model;
        Name = string.IsNullOrWhiteSpace(name) ? "OpenAI-compatible" : name;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
    }

    public async Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var model = request.Model ?? _defaultModel;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = request.Messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            }).ToArray(),
            ["temperature"] = request.Temperature,
            ["stream"] = false
        };
        if (request.JsonMode)
            payload["response_format"] = new { type = "json_object" };

        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        message.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = responseBody.Length > 1200
                ? responseBody[..1200]
                : responseBody;
            throw new HttpRequestException(
                $"{Name} returned HTTP {(int)response.StatusCode}: {detail}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"{Name} returned an empty response.");

        stopwatch.Stop();
        return new AIResponse(Name, content, model, stopwatch.Elapsed);
    }
}
