using System.Net.Http.Json;
using System.Text.Json;
using Council.App.Abaqus;
using Council.App.Finance;

namespace Council.App;

public sealed record RuntimeHealthCheckResult(string Component, string Status, string Detail);

/// <summary>
/// Read-only startup diagnostics. Checks local dependencies and configuration without
/// printing credentials or starting any background connection, GUI action, or solver.
/// </summary>
public static class RuntimeHealthCheck
{
    public static async Task<IReadOnlyList<RuntimeHealthCheckResult>> RunAsync(
        HttpClient? httpClient = null,
        Func<string, string?>? environment = null,
        Func<bool>? hasDhanCredentials = null,
        Func<AbaqusInstallationInfo>? detectAbaqus = null,
        CancellationToken cancellationToken = default)
    {
        var get = environment ?? Environment.GetEnvironmentVariable;
        var checks = new List<RuntimeHealthCheckResult>
        {
            new(".NET runtime", "READY", Environment.Version.ToString()),
            new("Operating system", OperatingSystem.IsWindows() ? "READY" : "UNSUPPORTED",
                OperatingSystem.IsWindows() ? "Windows desktop automation is supported." : "This build targets Windows desktop automation.")
        };

        var ownsClient = httpClient is null;
        httpClient ??= new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        try
        {
            var baseUrl = get("OLLAMA_BASE_URL") ?? "http://localhost:11434";
            var brainModel = get("OLLAMA_MODEL") ?? "qwen3.5:2b-q4_K_M";
            var visionModel = get("JARVIS_VISION_MODEL") ?? "qwen3-vl:2b";
            var documentModel = get("JARVIS_DOCUMENT_VISION_MODEL") ?? "glm-ocr";
            var provider = (get("JARVIS_AI_PROVIDER") ?? "").Trim().ToLowerInvariant();
            var visionProvider = (get("JARVIS_VISION_PROVIDER") ?? "ollama").Trim().ToLowerInvariant();
            var visionEnabled = !bool.TryParse(get("JARVIS_LOCAL_VISION_ENABLED"), out var enabled) || enabled;
            var brainRequired = provider == "ollama";
            var localVisionRequired = visionProvider == "ollama" && visionEnabled;
            var ollamaRequired = brainRequired || localVisionRequired;
            HashSet<string> installedModels = new(StringComparer.OrdinalIgnoreCase);
            string? ollamaError = null;

            try
            {
                var endpoint = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/tags");
                if (endpoint.Scheme is not ("http" or "https"))
                    throw new InvalidOperationException("Ollama URL must use HTTP or HTTPS.");
                using var response = await httpClient.GetAsync(endpoint, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("Ollama returned HTTP " + (int)response.StatusCode + ".");
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (body.Length > 2_000_000)
                    throw new InvalidDataException("Ollama model list exceeded the 2 MB diagnostic limit.");
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                {
                    foreach (var model in models.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        {
                            var value = name.GetString();
                            if (!string.IsNullOrWhiteSpace(value)) installedModels.Add(value);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or UriFormatException or InvalidDataException)
            {
                ollamaError = ex is TaskCanceledException ? "Ollama did not respond within 4 seconds." : ex.Message;
            }

            checks.Add(new("Ollama service", ollamaError is null ? "READY" : ollamaRequired ? "NOT READY" : "OPTIONAL",
                ollamaError is null ? $"Connected to {SafeEndpoint(baseUrl)}; found {installedModels.Count} installed model(s)." :
                ollamaRequired ? ollamaError : "Not required by the selected AI and vision providers."));
            checks.Add(ModelCheck("Local chat model", brainModel, installedModels, ollamaError, brainRequired));
            if (visionProvider == "ollama" && visionEnabled)
            {
                checks.Add(ModelCheck("Desktop / engineering vision", visionModel, installedModels, ollamaError, required: true));
                checks.Add(ModelCheck("Specialist document OCR", documentModel, installedModels, ollamaError, required: true));
            }
            else if (visionProvider == "gemini")
            {
                checks.Add(new("Desktop / engineering vision", "OPTIONAL", "Local Qwen vision is not used when Gemini vision is selected."));
                checks.Add(new("Specialist document OCR", "OPTIONAL", "Local GLM-OCR is not used when Gemini vision is selected."));
                checks.Add(Present("Cloud vision/document reader", get("GEMINI_API_KEY"), "GEMINI_API_KEY"));
            }
            else if (visionProvider == "none" || !visionEnabled)
            {
                checks.Add(new("Desktop / engineering vision", "DISABLED", "Local/cloud vision is disabled."));
                checks.Add(new("Specialist document OCR", "DISABLED", "Local/cloud document OCR is disabled."));
            }
            else
            {
                checks.Add(new("Desktop / engineering vision", "NOT READY", "JARVIS_VISION_PROVIDER must be ollama, gemini or none."));
                checks.Add(new("Specialist document OCR", "NOT READY", "JARVIS_VISION_PROVIDER must be ollama, gemini or none."));
            }


            var providerCheck = provider switch
            {
                "gemini" => Present("Gemini provider", get("GEMINI_API_KEY"), "GEMINI_API_KEY"),
                "openai" => Present("OpenAI provider", get("OPENAI_API_KEY"), "OPENAI_API_KEY"),
                "omniroute" => !string.IsNullOrWhiteSpace(get("OMNIROUTE_API_KEY")) &&
                               !string.IsNullOrWhiteSpace(get("OMNIROUTE_MODEL")) &&
                               !string.IsNullOrWhiteSpace(get("OMNIROUTE_FREE_MODELS"))
                    ? new("OmniRoute provider", "READY", "API key, explicit model and free-model allowlist are configured.")
                    : new("OmniRoute provider", "NOT READY", "Configure OMNIROUTE_API_KEY, OMNIROUTE_MODEL and OMNIROUTE_FREE_MODELS locally."),
                "ollama" => ModelCheck("Ollama chat provider", brainModel, installedModels, ollamaError),
                "mock" => new("AI provider", "OPTIONAL", "Mock provider explicitly selected; model responses are simulated."),
                "" => !string.IsNullOrWhiteSpace(get("GEMINI_API_KEY"))
                    ? new("AI provider", "READY", "Gemini will be selected automatically.")
                    : !string.IsNullOrWhiteSpace(get("OPENAI_API_KEY"))
                        ? new("AI provider", "READY", "OpenAI will be selected automatically.")
                        : new("AI provider", "OPTIONAL", "No cloud key configured; JARVIS starts in mock mode unless JARVIS_AI_PROVIDER=ollama is selected."),
                _ => new("AI provider", "NOT READY", "Unknown JARVIS_AI_PROVIDER value.")
            };
            checks.Add(providerCheck);

            var dhanConnected = (hasDhanCredentials ?? (() =>
                !string.IsNullOrWhiteSpace(DhanCredentialStore.ReadClientId()) &&
                !string.IsNullOrWhiteSpace(DhanCredentialStore.ReadAccessToken())))();
            checks.Add(new("Dhan read-only integration", dhanConnected ? "READY" : "OPTIONAL",
                dhanConnected ? "Credentials detected in local secure storage or environment; values are never displayed." :
                "Not configured. Optional; use scripts/Set-DhanCredentials.ps1 locally. Live feed also needs a Dhan Data API subscription."));

            var abaqus = (detectAbaqus ?? (() => new AbaqusInstallationDetector().Detect()))();
            checks.Add(new("Abaqus launcher", abaqus.IsAvailable ? "READY" : "OPTIONAL",
                abaqus.IsAvailable ? "Detected: " + abaqus.CommandPath : abaqus.Message));

            var whisperPath = get("JARVIS_WHISPER_MODEL_PATH") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS", "models", "ggml-small.bin");
            checks.Add(new("Local speech recognition", File.Exists(whisperPath) ? "READY" : "OPTIONAL",
                File.Exists(whisperPath) ? "Whisper model file is present." : "Whisper model not found; /voice may use the Windows speech fallback."));
            var piperPath = get("JARVIS_PIPER_PYTHON");
            checks.Add(new("Local speech synthesis", !string.IsNullOrWhiteSpace(piperPath) && File.Exists(piperPath) ? "READY" : "OPTIONAL",
                !string.IsNullOrWhiteSpace(piperPath) && File.Exists(piperPath) ? "Piper Python runtime is present." : "Piper runtime not configured; Windows speech fallback is available."));

            var bridgeConfigured = !string.IsNullOrWhiteSpace(get("JARVIS_BRIDGE_BIND")) &&
                                   !string.IsNullOrWhiteSpace(get("JARVIS_BRIDGE_TOKEN"));
            checks.Add(new("Android phone bridge", bridgeConfigured ? "READY" : "OPTIONAL",
                bridgeConfigured ? "Bridge configuration is present; it starts only when JARVIS runs." : "Disabled until both bind address and token are configured."));
        }
        finally
        {
            if (ownsClient) httpClient.Dispose();
        }

        return checks;
    }

    private static RuntimeHealthCheckResult ModelCheck(string component, string model,
        HashSet<string> installed, string? ollamaError, bool required)
    {
        if (ollamaError is not null)
            return new(component, required ? "NOT READY" : "OPTIONAL",
                required ? "Cannot verify model because Ollama is unavailable." : "Not required by the selected provider.");
        var found = installed.Contains(model) || installed.Contains(model + ":latest");
        return new(component, found ? "READY" : required ? "NOT READY" : "OPTIONAL",
            found ? $"Model '{model}' is installed." :
            required ? $"Model '{model}' is missing. Pull it with Ollama or run the appropriate JARVIS installer." :
            $"Model '{model}' is not installed; it is not required by the selected provider.");
    }

    private static RuntimeHealthCheckResult Present(string component, string? value, string variable) =>
        string.IsNullOrWhiteSpace(value)
            ? new(component, "NOT READY", $"Set {variable} locally; never put the key in source control.")
            : new(component, "READY", "Credential detected; value is not displayed.");

    private static string SafeEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "configured Ollama endpoint";
        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            ? uri.Authority
            : "configured Ollama host";
    }
}
