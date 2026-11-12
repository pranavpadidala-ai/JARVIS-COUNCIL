using System.Net.Http.Json;
using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// Local-only image understanding through an Ollama vision model.
/// Images are posted to the configured local Ollama endpoint; no cloud fallback is used.
/// </summary>
public sealed class OllamaVisionProvider : IImageUnderstandingProvider
{
    private const int MaxImages = 4;
    private const int MaxImageBytes = 8 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _contextTokens;

    public OllamaVisionProvider(
        string model = "moondream",
        string baseUrl = "http://localhost:11434",
        int contextTokens = 2048,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("A local vision model name is required.", nameof(model));
        if (contextTokens < 512 || contextTokens > 8192)
            throw new ArgumentOutOfRangeException(nameof(contextTokens));

        _model = model;
        _contextTokens = contextTokens;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public async Task<string> DescribeImagesAsync(
        string prompt,
        IReadOnlyList<AIImage> images,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("An image-analysis prompt is required.", nameof(prompt));
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0 || images.Count > MaxImages)
            throw new ArgumentOutOfRangeException(nameof(images), $"Supply between 1 and {MaxImages} images.");

        var totalBytes = 0;
        var encodedImages = new string[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i] ?? throw new ArgumentException("Image entries cannot be null.", nameof(images));
            if (image.Data is null || image.Data.Length == 0)
                throw new ArgumentException("Image data cannot be empty.", nameof(images));
            if (image.MimeType is not ("image/png" or "image/jpeg" or "image/webp"))
                throw new ArgumentException("Only PNG, JPEG, and WebP images are supported.", nameof(images));

            totalBytes = checked(totalBytes + image.Data.Length);
            if (totalBytes > MaxImageBytes)
                throw new ArgumentException("The combined image payload exceeds 8 MB.", nameof(images));
            encodedImages[i] = Convert.ToBase64String(image.Data);
        }

        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt + Environment.NewLine +
                        "Image labels: " + string.Join("; ", images.Select(image => image.Name)),
                    images = encodedImages
                }
            },
            stream = false,
            keep_alive = "2m",
            options = new
            {
                temperature = 0.1,
                num_ctx = _contextTokens
            }
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("api/chat", payload, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Cannot connect to local Ollama at " + _http.BaseAddress +
                ". Install/start Ollama and pull the vision model with scripts/Install-LocalVision.ps1. " +
                ex.Message, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    "Local Ollama vision request failed with HTTP " + (int)response.StatusCode + ": " +
                    body[..Math.Min(body.Length, 1200)]);

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException(
                    "Ollama returned an unexpected vision response. Check the installed model tag.");

            var result = content.GetString();
            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("The local vision model returned an empty response.");
            return result.Trim();
        }
    }
}
