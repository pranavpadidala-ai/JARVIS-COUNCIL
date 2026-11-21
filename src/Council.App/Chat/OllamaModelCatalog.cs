using System.Text.Json;

namespace Council.App.Chat;

/// <summary>
/// Lists the models installed in the local Ollama service so the desktop model
/// selector can offer them instead of asking the user to type an exact ID.
/// Returns an empty list (never throws) when Ollama is not running.
/// </summary>
public static class OllamaModelCatalog
{
    public static async Task<IReadOnlyList<string>> ListInstalledAsync(
        string? baseUrl = null,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl)
            ? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434"
            : baseUrl;
        var ownsClient = httpClient is null;
        var http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        try
        {
            var endpoint = new Uri(new Uri(root.TrimEnd('/') + "/"), "api/tags");
            using var response = await http.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<string>();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("models", out var models) ||
                models.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return models.EnumerateArray()
                .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return Array.Empty<string>();
        }
        finally
        {
            if (ownsClient)
                http.Dispose();
        }
    }

    /// <summary>
    /// Vision and OCR models cannot hold a normal chat; they are listed last so the
    /// chat model is the obvious first choice.
    /// </summary>
    public static IReadOnlyList<string> OrderForChat(IEnumerable<string> models) =>
        models.OrderBy(IsSpecialistModel).ThenBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsSpecialistModel(string model) =>
        model.Contains("-vl", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("ocr", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("embed", StringComparison.OrdinalIgnoreCase);
}
