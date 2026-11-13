using System.Net;
using System.Text;
using Council.App;
using Council.App.Abaqus;

namespace Council.Dev.Tests;

public sealed class RuntimeHealthCheckTests
{
    [Fact]
    public async Task ReportsInstalledLocalModelsAndConfiguredOptionalServices()
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"models":[{"name":"qwen3.5:2b-q4_K_M"},{"name":"qwen3-vl:2b"},{"name":"glm-ocr"}]}""", Encoding.UTF8, "application/json")
        })));
        var env = new Dictionary<string, string?>
        {
            ["OLLAMA_BASE_URL"] = "http://localhost:11434",
            ["JARVIS_AI_PROVIDER"] = "ollama",
            ["OLLAMA_MODEL"] = "qwen3.5:2b-q4_K_M",
            ["JARVIS_VISION_MODEL"] = "qwen3-vl:2b",
            ["JARVIS_DOCUMENT_VISION_MODEL"] = "glm-ocr",
            ["JARVIS_WHISPER_MODEL_PATH"] = null,
            ["JARVIS_PIPER_PYTHON"] = null
        };

        var checks = await RuntimeHealthCheck.RunAsync(
            client, key => env.GetValueOrDefault(key),
            hasDhanCredentials: () => true,
            detectAbaqus: () => new AbaqusInstallationInfo(true, @"C:\SIMULIA\Commands\abq2024.bat", "found"));

        Assert.All(checks.Where(x => x.Component.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                                     x.Component.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
                                     x.Component.Contains("OCR", StringComparison.OrdinalIgnoreCase)),
            item => Assert.Equal("READY", item.Status));
        Assert.Contains(checks, x => x.Component == "Dhan read-only integration" && x.Status == "READY");
        Assert.Contains(checks, x => x.Component == "Abaqus launcher" && x.Status == "READY");
        Assert.Contains(checks, x => x.Component == "Android phone bridge" && x.Status == "OPTIONAL");
    }

    [Fact]
    public async Task ReportsMissingOllamaWithoutExposingCredentialValues()
    {
        using var client = new HttpClient(new StubHandler((_, _) => throw new HttpRequestException("offline")));
        var env = new Dictionary<string, string?>
        {
            ["JARVIS_AI_PROVIDER"] = "gemini",
            ["GEMINI_API_KEY"] = "secret-test-value",
            ["OLLAMA_MODEL"] = "qwen3.5:2b-q4_K_M"
        };
        var checks = await RuntimeHealthCheck.RunAsync(client, key => env.GetValueOrDefault(key),
            hasDhanCredentials: () => false,
            detectAbaqus: () => new AbaqusInstallationInfo(false, null, "not found"));

        Assert.Contains(checks, x => x.Component == "Ollama service" && x.Status == "NOT READY");
        Assert.Contains(checks, x => x.Component == "Local chat model" && x.Status == "NOT READY");
        Assert.Contains(checks, x => x.Component == "Gemini provider" && x.Status == "READY");
        Assert.DoesNotContain(checks, x => x.Detail.Contains("secret-test-value", StringComparison.Ordinal));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
