using System.Diagnostics;
using Council.Core.Models;
using Council.Core.Providers;
using Google.GenAI;
using Google.GenAI.Types;

namespace Council.AI.Providers;

public sealed class GeminiProvider : IAIProvider, IImageUnderstandingProvider, IDocumentUnderstandingProvider
{
    private readonly Client _client;
    private readonly string _defaultModel;

    public string Name => "Gemini";

    public GeminiProvider(
        string apiKey,
        string model = "gemini-3.6-flash")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "Gemini API key is required.", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException(
                "Gemini model is required.", nameof(model));

        _client = new Client(apiKey: apiKey);
        _defaultModel = model;
    }

    public async Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException(
                "At least one message is required.", nameof(request));

        var stopwatch = Stopwatch.StartNew();
        var model = request.Model ?? _defaultModel;

        var systemMessages = request.Messages
            .Where(m =>
                IsRole(m.Role, "system") ||
                IsRole(m.Role, "developer"))
            .Select(m => m.Content);

        var systemInstruction = string.Join(
            "\n\n", systemMessages);

        var contents = new List<Content>();

        foreach (var message in request.Messages)
        {
            var role = message.Role?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(message.Content))
                continue;

            if (role is "system" or "developer")
                continue;

            if (role is not ("user" or "assistant" or "model"))
                throw new ArgumentException(
                    $"Unsupported Gemini message role: {message.Role}");

            contents.Add(new Content
            {
                Role = role == "user" ? "user" : "model",
                Parts = [new Part { Text = message.Content }]
            });
        }

        if (contents.Count == 0 ||
            !contents.Any(c => c.Role == "user"))
            throw new ArgumentException(
                "At least one user message is required.", nameof(request));

        var config = new GenerateContentConfig
        {
            Temperature = request.Temperature
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            config.SystemInstruction = new Content
            {
                Parts = [new Part { Text = systemInstruction }]
            };
        }

        var response = await _client.Models.GenerateContentAsync(
            model,
            contents,
            config,
            cancellationToken);

        stopwatch.Stop();

        var output = response.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .Select(p => p.Text)
            .ToList();

        var content = output is { Count: > 0 }
            ? string.Join("", output)
            : null;

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                "Gemini returned an empty response.");

        return new AIResponse(
            Provider: Name,
            Content: content,
            Model: model,
            Duration: stopwatch.Elapsed);
    }

    public async Task<string> DescribeImagesAsync(
        string prompt,
        IReadOnlyList<AIImage> images,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("An image analysis prompt is required.", nameof(prompt));
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
            throw new ArgumentException("At least one image is required.", nameof(images));
        if (images.Count > 12)
            throw new ArgumentException("A maximum of 12 images can be analysed in one request.", nameof(images));

        var parts = new List<Part> { new Part { Text = prompt } };
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (image.Data is null || image.Data.Length == 0)
                continue;
            if (image.Data.Length > 8 * 1024 * 1024)
                throw new ArgumentException("An image exceeds the 8 MB per-image limit.", nameof(images));
            if (image.MimeType is not ("image/png" or "image/jpeg" or "image/webp"))
                throw new ArgumentException("Only PNG, JPEG, and WebP images are supported.", nameof(images));

            parts.Add(new Part
            {
                Text = "Image source: " + image.Name
            });
            parts.Add(new Part
            {
                InlineData = new Blob
                {
                    Data = image.Data,
                    MimeType = image.MimeType
                }
            });
        }

        if (parts.Count == 1)
            throw new ArgumentException("No supported image data was supplied.", nameof(images));

        var response = await _client.Models.GenerateContentAsync(
            _defaultModel,
            [new Content { Role = "user", Parts = parts }],
            new GenerateContentConfig { Temperature = 0.1 },
            cancellationToken);

        var text = response.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .Where(part => !string.IsNullOrWhiteSpace(part.Text))
            .Select(part => part.Text)
            .ToList();

        var result = text is { Count: > 0 } ? string.Join("", text) : null;
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Gemini returned no image analysis text.");
        return result;
    }


    public async Task<string> AnalyzeDocumentAsync(string prompt, AIImage document, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("A document-analysis prompt is required.", nameof(prompt));
        ArgumentNullException.ThrowIfNull(document);
        if (document.MimeType != "application/pdf") throw new ArgumentException("Only PDF documents are supported.", nameof(document));
        if (document.Data is null || document.Data.Length == 0) throw new ArgumentException("PDF data cannot be empty.", nameof(document));
        if (document.Data.Length > 14 * 1024 * 1024) throw new ArgumentException("PDF exceeds the 14 MB inline-analysis limit.", nameof(document));
        var parts = new List<Part>
        {
            new Part { Text = prompt + System.Environment.NewLine + "Document name: " + document.Name },
            new Part { InlineData = new Blob { Data = document.Data, MimeType = "application/pdf" } }
        };
        var response = await _client.Models.GenerateContentAsync(_defaultModel,
            [new Content { Role = "user", Parts = parts }],
            new GenerateContentConfig { Temperature = 0.1 }, cancellationToken);
        var text = response.Candidates?.FirstOrDefault()?.Content?.Parts?
            .Where(part => !string.IsNullOrWhiteSpace(part.Text)).Select(part => part.Text).ToList();
        var result = text is { Count: > 0 } ? string.Join("", text) : null;
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("Gemini returned no PDF document analysis text.");
        return result;
    }

    private static bool IsRole(string? actual, string expected) =>
        string.Equals(
            actual, expected,
            StringComparison.OrdinalIgnoreCase);
}
