using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// OpenAI-compatible client for a local OmniRoute gateway.
/// Free-tier safety is enforced by requiring an explicit model prefix allowlist.
/// The special "auto" model is deliberately rejected because its cost cannot be
/// guaranteed by the client.
/// </summary>
public sealed class OmniRouteProvider : IAIProvider
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HashSet<string> _allowedPrefixes;

    public string Name => "OmniRoute (allowlisted models)";

    public OmniRouteProvider(
        string apiKey,
        string model,
        string baseUrl,
        string freeModelPrefixes,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OmniRoute API key is required.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("An explicit OmniRoute model is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A valid HTTP(S) OmniRoute base URL is required.", nameof(baseUrl));

        if (!baseUri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("OmniRoute base URL must end in /v1.", nameof(baseUrl));

        if (baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback)
            throw new ArgumentException(
                "A remote OmniRoute gateway must use HTTPS so its API key is not sent in clear text.",
                nameof(baseUrl));

        _allowedPrefixes = (freeModelPrefixes ?? string.Empty)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_allowedPrefixes.Count == 0 ||
            _allowedPrefixes.Any(prefix => prefix.Contains('/') || prefix.Equals("auto", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(
                "Provide one or more explicit free-provider prefixes separated by semicolons. Do not use 'auto'.",
                nameof(freeModelPrefixes));

        _apiKey = apiKey;
        _baseUri = baseUri;
        _model = model;
        ValidateAllowedModel(model);

        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    public async Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var model = request.Model ?? _model;
        ValidateAllowedModel(model);

        var messages = request.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new
            {
                role = NormalizeRole(message.Role),
                content = message.Content
            })
            .ToArray();

        if (messages.Length == 0 || !messages.Any(message => message.role == "user"))
            throw new ArgumentException("At least one user message is required.", nameof(request));

        var payload = new
        {
            model,
            messages,
            temperature = request.Temperature,
            stream = false
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post, new Uri(_baseUri, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        httpRequest.Content = JsonContent.Create(payload);

        var timer = Stopwatch.StartNew();
        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"OmniRoute returned HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 1200)]}");

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var messageNode) ||
            !messageNode.TryGetProperty("content", out var contentNode))
            throw new InvalidOperationException("OmniRoute returned an unexpected Chat Completions response.");

        var content = ReadContent(contentNode);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("OmniRoute returned an empty response.");

        var responseModel = document.RootElement.TryGetProperty("model", out var modelNode) &&
                            modelNode.ValueKind == JsonValueKind.String
            ? modelNode.GetString()
            : model;

        timer.Stop();
        return new AIResponse(Name, content, responseModel, timer.Elapsed);
    }

    private void ValidateAllowedModel(string model)
    {
        var separator = model.IndexOf('/');
        if (separator <= 0 || separator == model.Length - 1)
            throw new InvalidOperationException(
                "Free-only routing requires an explicit provider/model ID such as free-provider/model-name. The 'auto' alias is blocked.");

        var prefix = model[..separator];
        if (!_allowedPrefixes.Contains(prefix))
            throw new InvalidOperationException(
                $"OmniRoute model prefix '{prefix}' is not in OMNIROUTE_FREE_MODEL_PREFIXES. Request blocked to prevent accidental paid routing.");
    }

    private static string NormalizeRole(string role)
    {
        var normalized = role?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "system" or "developer" or "user" or "assistant" => normalized,
            _ => throw new ArgumentException($"Unsupported OmniRoute message role: {role}")
        };
    }

    private static string ReadContent(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String)
            return node.GetString() ?? string.Empty;

        if (node.ValueKind == JsonValueKind.Array)
        {
            return string.Join("", node.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object &&
                               part.TryGetProperty("text", out var text) &&
                               text.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString()));
        }

        return string.Empty;
    }
}
