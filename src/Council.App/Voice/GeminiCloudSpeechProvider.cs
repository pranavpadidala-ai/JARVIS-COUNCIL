using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Council.App.Voice;

/// <summary>
/// Optional cloud transcription and neural TTS using the Gemini API. Audio uploads
/// are deleted after transcription where the API permits; local Whisper/Piper remain fallbacks.
/// </summary>
public sealed class GeminiCloudSpeechProvider
{
    private const string ApiBase = "https://generativelanguage.googleapis.com";
    private const int MaxAudioBytes = 8 * 1024 * 1024;
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly string[] EngineeringVocabulary =
    [
        "Abaqus", "ETABS", "mullion", "finned mullion", "shear wall", "Mivan", "NIT Tiruchirappalli",
        "IS 456", "IS 800", "IS 1893", "IS 875", "Riks", "S4R", "buckling", "torsion",
        "NSE", "BSE", "NIFTY", "BANKNIFTY", "FII", "DII", "VCP", "STT", "JARVIS"
    ];

    private readonly string _apiKey;
    private readonly string _transcribeModel;
    private readonly string _ttsModel;
    private readonly string _voice;
    private readonly HttpClient _http;

    public GeminiCloudSpeechProvider(
        string apiKey,
        string transcribeModel = "gemini-3.5-transcribe",
        string ttsModel = "gemini-3.8-flash-lite-tts",
        string voice = "Kore",
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Gemini API key is required.", nameof(apiKey));
        _apiKey = apiKey;
        _transcribeModel = string.IsNullOrWhiteSpace(transcribeModel) ? "gemini-3.5-transcribe" : transcribeModel.Trim();
        _ttsModel = string.IsNullOrWhiteSpace(ttsModel) ? "gemini-3.8-flash-lite-tts" : ttsModel.Trim();
        _voice = string.IsNullOrWhiteSpace(voice) ? "Kore" : voice.Trim();
        _http = httpClient ?? SharedClient;
    }

    public async Task<string?> TranscribeAudioAsync(byte[] wavAudio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wavAudio);
        if (wavAudio.Length == 0 || wavAudio.Length > MaxAudioBytes)
            throw new ArgumentException("Captured WAV audio must be between 1 byte and 8 MB.", nameof(wavAudio));

        var uploaded = await UploadAudioAsync(wavAudio, cancellationToken);
        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { fileData = new { fileUri = uploaded.Uri, mimeType = "audio/wav" } }
                        }
                    }
                },
                generationConfig = new
                {
                    audioTranscriptionConfig = new
                    {
                        mode = "VERBATIM",
                        customVocabulary = EngineeringVocabulary
                    }
                }
            };
            using var response = await PostJsonAsync(
                $"{ApiBase}/v1beta/models/{Uri.EscapeDataString(_transcribeModel)}:generateContent",
                payload, cancellationToken);
            if (!response.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
                return null;
            var parts = candidates[0].TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var contentParts) &&
                        contentParts.ValueKind == JsonValueKind.Array
                ? contentParts.EnumerateArray()
                    .Where(part => part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    .Select(part => part.GetProperty("text").GetString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray()
                : [];
            var transcript = string.Join("", parts).Trim();
            return transcript.Length == 0 ? null : transcript;
        }
        finally
        {
            await DeleteUploadedFileAsync(uploaded.Name);
        }
    }

    public async Task<byte[]> SynthesizeSpeechAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (text.Length > 4000)
            throw new ArgumentException("Cloud TTS accepts up to 4,000 characters per request; use local Piper for longer responses.", nameof(text));
        var payload = new
        {
            model = _ttsModel,
            input = new[]
            {
                new
                {
                    type = "user_input",
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text,
                            annotations = new[]
                            {
                                new
                                {
                                    type = "speech_metadata",
                                    style = "Clear, natural, calm conversational Indian English. Moderate pace. Articulate engineering terms, numbers and units precisely. Preserve the exact wording."
                                }
                            }
                        }
                    }
                }
            },
            response_format = new { type = "audio" },
            generation_config = new { speech_config = new[] { new { voice = _voice } } }
        };
        using var response = await PostJsonAsync($"{ApiBase}/v1beta/interactions", payload, cancellationToken);
        if (!response.RootElement.TryGetProperty("output_audio", out var outputAudio) ||
            !outputAudio.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Gemini TTS response did not contain audio data.");
        var bytes = Convert.FromBase64String(data.GetString()!);
        if (bytes.Length == 0 || bytes.Length > MaxResponseBytes)
            throw new InvalidOperationException("Gemini TTS audio was empty or exceeded the 2 MB playback limit.");
        return bytes;
    }

    private async Task<UploadedFile> UploadAudioAsync(byte[] bytes, CancellationToken token)
    {
        using var start = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/upload/v1beta/files");
        start.Headers.Add("x-goog-api-key", _apiKey);
        start.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        start.Headers.Add("X-Goog-Upload-Command", "start");
        start.Headers.Add("X-Goog-Upload-Header-Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Headers.Add("X-Goog-Upload-Header-Content-Type", "audio/wav");
        start.Content = new StringContent(
            JsonSerializer.Serialize(new { file = new { display_name = "JARVIS voice capture" } }),
            Encoding.UTF8, "application/json");
        using var startResponse = await _http.SendAsync(start, HttpCompletionOption.ResponseHeadersRead, token);
        await EnsureSuccessAsync(startResponse, token);
        if (!startResponse.Headers.TryGetValues("x-goog-upload-url", out var uploadUrls))
            throw new InvalidOperationException("Gemini Files API did not return a resumable upload URL.");
        var uploadUrl = uploadUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(uploadUrl)) throw new InvalidOperationException("Gemini Files API returned an empty upload URL.");

        using var upload = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        upload.Headers.Add("X-Goog-Upload-Offset", "0");
        upload.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        upload.Content = new ByteArrayContent(bytes);
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var uploadResponse = await _http.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, token);
        var body = await ReadBoundedBodyAsync(uploadResponse, token);
        if (!uploadResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini audio upload failed with HTTP {(int)uploadResponse.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var file = document.RootElement.TryGetProperty("file", out var nested) ? nested : document.RootElement;
        var name = file.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var uri = file.TryGetProperty("uri", out var uriElement) ? uriElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException("Gemini Files API response did not contain a file name and URI.");
        return new UploadedFile(name, uri);
    }

    private async Task<JsonDocument> PostJsonAsync(string url, object payload, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        var body = await ReadBoundedBodyAsync(response, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API returned HTTP {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private async Task DeleteUploadedFileAsync(string name)
    {
        try
        {
            var resourceName = name.StartsWith("files/", StringComparison.OrdinalIgnoreCase) ? name : "files/" + name;
            var encodedName = string.Join("/", resourceName.Split('/').Select(Uri.EscapeDataString));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBase}/v1beta/{encodedName}");
            request.Headers.Add("x-goog-api-key", _apiKey);
            using var response = await _http.SendAsync(request, timeout.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await ReadBoundedBodyAsync(response, token);
        throw new HttpRequestException($"Gemini Files API upload session failed with HTTP {(int)response.StatusCode}: {body}");
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        return body.Length > 4000 ? body[..4000] : body;
    }

    private sealed record UploadedFile(string Name, string Uri);
}
