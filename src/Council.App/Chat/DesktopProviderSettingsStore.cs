using System.Text.Json;

namespace Council.App.Chat;

/// <summary>
/// Persists only the selected provider and model for the desktop app.
/// API keys and other credentials are deliberately never read into or written
/// by this settings file; provider credentials remain in the existing secure
/// environment / credential configuration.
/// </summary>
public sealed record DesktopProviderSelection(string Provider, string Model);

public static class DesktopProviderSettingsStore
{
    private const int MaximumSettingsBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetSettingsPath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = AppContext.BaseDirectory;
        return Path.Combine(localData, "JARVIS", "desktop-settings.json");
    }

    public static DesktopProviderSelection GetCurrent()
    {
        var provider = Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider))
        {
            // Local-first: with nothing chosen, JARVIS uses Ollama (matching DesktopProviderFactory)
            // instead of silently answering from the Mock test provider.
            provider = "ollama";
        }

        return new DesktopProviderSelection(provider, GetCurrentModel(provider));
    }

    public static DesktopProviderSelection? TryLoadAndApplySaved(out string message, string? settingsPath = null)
    {
        var path = settingsPath ?? GetSettingsPath();
        if (!File.Exists(path))
        {
            message = "No saved desktop provider selection exists.";
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumSettingsBytes)
                throw new InvalidDataException("The desktop settings file exceeds the allowed size.");

            var json = File.ReadAllText(path);
            var selection = JsonSerializer.Deserialize<DesktopProviderSelection>(json)
                ?? throw new InvalidDataException("The desktop settings file is empty.");
            var normalized = Validate(selection.Provider, selection.Model);
            ApplyToProcess(normalized);
            message = "Saved provider settings loaded.";
            return normalized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            message = "Warning: saved desktop provider settings were ignored. " + ex.GetBaseException().Message;
            return null;
        }
    }

    public static void SaveAndApply(string provider, string? model, string? settingsPath = null)
    {
        var selection = Validate(provider, model);
        var path = settingsPath ?? GetSettingsPath();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("A settings directory is required.", nameof(settingsPath));

        Directory.CreateDirectory(directory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(selection, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
            ApplyToProcess(selection);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static string GetDefaultModel(string provider)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "ollama" => Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "qwen3.5:2b-q4_K_M",
            "gemini" => Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.6-flash",
            "openai" => Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.2",
            "omniroute" => Environment.GetEnvironmentVariable("OMNIROUTE_MODEL") ?? string.Empty,
            "mock" => string.Empty,
            _ => throw new ArgumentException("Choose ollama, gemini, openai, omniroute or mock.", nameof(provider))
        };
    }

    public static string GetDisplayName(string provider) =>
        provider.Trim().ToLowerInvariant() switch
        {
            "ollama" => "Local Ollama",
            "gemini" => "Google Gemini",
            "openai" => "OpenAI",
            "omniroute" => "OmniRoute",
            "mock" => "Mock / test mode",
            _ => "Unknown provider"
        };

    private static string GetCurrentModel(string provider)
    {
        var variable = provider switch
        {
            "ollama" => "OLLAMA_MODEL",
            "gemini" => "GEMINI_MODEL",
            "openai" => "OPENAI_MODEL",
            "omniroute" => "OMNIROUTE_MODEL",
            "mock" => null,
            _ => null
        };
        return variable is null
            ? string.Empty
            : Environment.GetEnvironmentVariable(variable) ?? GetDefaultModel(provider);
    }

    private static DesktopProviderSelection Validate(string provider, string? model)
    {
        var normalizedProvider = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProvider is not ("ollama" or "gemini" or "openai" or "omniroute" or "mock"))
            throw new ArgumentException("Choose Ollama, Gemini, OpenAI, OmniRoute or Mock.", nameof(provider));

        var normalizedModel = (model ?? string.Empty).Trim();
        if (normalizedProvider == "mock")
            normalizedModel = string.Empty;
        else if (normalizedModel.Length is 0 or > 160 ||
                 normalizedModel.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            throw new ArgumentException("Enter a model ID of 1–160 characters with no spaces or control characters.", nameof(model));

        if (normalizedProvider == "omniroute")
        {
            if (!normalizedModel.Contains('/') ||
                normalizedModel.Equals("auto", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("OmniRoute requires an exact provider/model ID; the auto alias is blocked.", nameof(model));

            var allowedModels = (Environment.GetEnvironmentVariable("OMNIROUTE_FREE_MODELS") ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowedModels.Contains(normalizedModel, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "This OmniRoute model is not in OMNIROUTE_FREE_MODELS. Add only an exact model ID you have verified as free.",
                    nameof(model));
        }

        return new DesktopProviderSelection(normalizedProvider, normalizedModel);
    }

    private static void ApplyToProcess(DesktopProviderSelection selection)
    {
        Environment.SetEnvironmentVariable("JARVIS_AI_PROVIDER", selection.Provider);
        var modelVariable = selection.Provider switch
        {
            "ollama" => "OLLAMA_MODEL",
            "gemini" => "GEMINI_MODEL",
            "openai" => "OPENAI_MODEL",
            "omniroute" => "OMNIROUTE_MODEL",
            _ => null
        };

        if (modelVariable is not null)
            Environment.SetEnvironmentVariable(modelVariable, selection.Model);
    }
}
