using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// Connects to a locally running Ollama server. No cloud API key is used.
/// </summary>
public sealed class OllamaProvider : IAIProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _contextTokens;

    public string Name => "Ollama (local)";

    public OllamaProvider(
        string model = "qwen3.5:2b-q4_K_M",
        string baseUrl = "http://localhost:11434",
        int contextTokens = 4096,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("A local Ollama model name is required.", nameof(model));
        if (contextTokens < 1024 || contextTokens > 32768)
            throw new ArgumentOutOfRangeException(nameof(contextTokens));

        _model = model;
        _contextTokens = contextTokens;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public async Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var payload = new
        {
            model = _model,
            messages = request.Messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            }).ToArray(),
            stream = false,
            options = new
            {
                temperature = request.Temperature,
                num_ctx = _contextTokens
            }
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(
                "api/chat", payload, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Cannot connect to Ollama at " + _http.BaseAddress +
                ". Start Ollama and confirm that the local model is installed. " + ex.Message, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    "Ollama returned HTTP " + (int)response.StatusCode + ": " +
                    body[..Math.Min(body.Length, 1200)]);

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException(
                    "Ollama returned an unexpected response. Check the installed model and Ollama version.");

            timer.Stop();
            return new AIResponse(
                Name,
                content.GetString() ?? string.Empty,
                _model,
                timer.Elapsed);
        }
    }
}
