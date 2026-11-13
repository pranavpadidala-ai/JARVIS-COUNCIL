using System.Net;
using System.Text;
using System.Text.Json;
using Council.App.Voice;

namespace Council.Dev.Tests;

public sealed class GeminiCloudSpeechProviderTests
{
    [Fact]
    public async Task TranscribesAudioAndDeletesTemporaryUpload()
    {
        var calls = new List<string>();
        var handler = new StubHandler(async (request, token) =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/upload/v1beta/files" && request.Method == HttpMethod.Post)
            {
                Assert.Equal("resumable", request.Headers.GetValues("X-Goog-Upload-Protocol").Single());
                var start = new HttpResponseMessage(HttpStatusCode.OK);
                start.Headers.Add("x-goog-upload-url", "https://upload.test/session");
                return start;
            }
            if (request.RequestUri.Host == "upload.test")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"file":{"name":"files/test-audio","uri":"https://generativelanguage.googleapis.com/v1beta/files/test-audio"}}""")
                };
            if (request.RequestUri.AbsolutePath.EndsWith(":generateContent", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var fileUri = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("fileData").GetProperty("fileUri").GetString();
                Assert.Equal("https://generativelanguage.googleapis.com/v1beta/files/test-audio", fileUri);
                Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"Change the fin thickness to three millimetres."}]}}]}""")
                };
            }
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.OK);
            throw new InvalidOperationException("Unexpected HTTP request: " + request.RequestUri);
        });
        using var client = new HttpClient(handler);
        var provider = new GeminiCloudSpeechProvider("test-key", httpClient: client);
        var transcript = await provider.TranscribeAudioAsync([1, 2, 3]);

        Assert.Equal("Change the fin thickness to three millimetres.", transcript);
        Assert.Equal(4, calls.Count);
        Assert.Contains("DELETE /v1beta/files/test-audio", calls);
    }

    [Fact]
    public async Task SynthesizesWavUsingGeminiInteractionsApi()
    {
        var audio = Encoding.ASCII.GetBytes("RIFF-test-WAVE");
        var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1beta/interactions", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("gemini-3.8-flash-lite-tts", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("Kore", body.RootElement.GetProperty("generation_config").GetProperty("speech_config")[0].GetProperty("voice").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { output_audio = new { data = Convert.ToBase64String(audio) } }))
            };
        });
        using var client = new HttpClient(handler);
        var provider = new GeminiCloudSpeechProvider("test-key", httpClient: client);
        var result = await provider.SynthesizeSpeechAsync("Hello from JARVIS.");
        Assert.Equal(audio, result);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
