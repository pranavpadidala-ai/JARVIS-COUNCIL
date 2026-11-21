namespace Council.App.Voice;

/// <summary>
/// One place that decides where local voice models (Whisper, Piper) live, so the
/// installer and the app agree. Order:
///   1. JARVIS_MODELS_ROOT, when set (Install-LocalModels.ps1 sets it).
///   2. Models\LocalAI inside the JARVIS repository the app is running from, when it exists.
///   3. %LOCALAPPDATA%\JARVIS (the original location).
/// </summary>
public static class LocalModelPaths
{
    public static string ResolveRoot(Func<string, string?>? environment = null, string? startDirectory = null)
    {
        var env = environment ?? Environment.GetEnvironmentVariable;
        var configured = env("JARVIS_MODELS_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        var repoModels = FindRepositoryModelsRoot(startDirectory ?? AppContext.BaseDirectory);
        if (repoModels is not null)
            return repoModels;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS");
    }

    private static string? FindRepositoryModelsRoot(string startDirectory)
    {
        try
        {
            for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "JARVIS.Council.sln")))
                {
                    var candidate = Path.Combine(directory.FullName, "Models", "LocalAI");
                    return Directory.Exists(candidate) ? candidate : null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return null;
    }
}
