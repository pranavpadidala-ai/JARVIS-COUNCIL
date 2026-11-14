using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Council.Desktop.Voice;

/// <summary>
/// Lifecycle adapter for the optional local Python/Sherpa-ONNX wake-word worker.
/// Python owns microphone capture and keyword spotting; WPF owns start/stop/UI.
/// </summary>
public sealed class WakeWordService
{
    private const string ModelName = "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01";

    public static string ModelDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("JARVIS_WAKEWORD_MODEL_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(configured);

            var workspace = Environment.GetEnvironmentVariable("JARVIS_WORKSPACE_ROOT");
            if (string.IsNullOrWhiteSpace(workspace))
                workspace = Directory.GetCurrentDirectory();

            var repositoryModel = Path.Combine(
                workspace, "Models", "LocalAI", "WakeWord", ModelName);
            if (Directory.Exists(repositoryModel))
                return repositoryModel;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JARVIS", "models", ModelName);
        }
    }

    public static bool IsInstalled(out string detail)
    {
        if (!OperatingSystem.IsWindows())
        {
            detail = "Local wake-word listening is available on Windows only.";
            return false;
        }

        if (!Directory.Exists(ModelDirectory))
        {
            detail = "Wake-word model folder is missing: " + ModelDirectory;
            return false;
        }

        var requiredFiles = new[]
        {
            "encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
            "decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
            "joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
            "tokens.txt",
            "keywords.txt"
        };

        foreach (var file in requiredFiles)
        {
            var path = Path.Combine(ModelDirectory, file);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                detail = "Required wake-word asset is missing or empty: " + path;
                return false;
            }
        }

        if (ResolvePython() is null)
        {
            detail = "The isolated Python wake-word environment is missing. Run scripts/Install-WakeWord.ps1.";
            return false;
        }

        if (ResolveListenerScript() is null)
        {
            detail = "The Python wake-word listener script is missing from the JARVIS installation.";
            return false;
        }

        detail = "The local Python wake-word model and runtime are ready.";
        return true;
    }

    /// <summary>
    /// Runs one keyword-listening session. Cancelling kills the child process,
    /// which closes its audio stream and releases the microphone.
    /// </summary>
    public async Task<string> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Wake-word microphone capture is available on Windows only.");

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInstalled(out var detail))
            throw new InvalidOperationException(detail);

        var python = ResolvePython()!;
        var script = ResolveListenerScript()!;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = ResolveWorkspace(),
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add("--model-dir");
        process.StartInfo.ArgumentList.Add(ModelDirectory);
        process.StartInfo.Environment["PYTHONUTF8"] = "1";
        process.StartInfo.Environment["PYTHONUNBUFFERED"] = "1";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
                throw new InvalidOperationException("Could not start the Python wake-word listener.");

            var outputTask = process.StandardOutput.ReadLineAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Python wake-word listener failed: " + Trim(error, 1600));

            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException(
                    "Python wake-word listener exited without a keyword result. " + Trim(error, 800));

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (!root.TryGetProperty("event", out var eventElement) ||
                !string.Equals(eventElement.GetString(), "wake", StringComparison.Ordinal))
                throw new InvalidOperationException("Python wake-word listener returned an unexpected response.");

            if (!root.TryGetProperty("keyword", out var keywordElement))
                throw new InvalidOperationException("Python wake-word listener did not return a keyword.");

            var keyword = keywordElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
                throw new InvalidOperationException("Python wake-word listener returned an empty keyword.");

            return keyword;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or
                                   System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            TryKill(process);
            throw new InvalidOperationException(
                "Wake-word listener stopped safely: " + ex.GetBaseException().Message, ex);
        }
    }

    private static string ResolveWorkspace()
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_WORKSPACE_ROOT");
        return !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
            ? Path.GetFullPath(configured)
            : Directory.GetCurrentDirectory();
    }

    private static string? ResolvePython()
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_WAKEWORD_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var workspacePython = Path.Combine(
            ResolveWorkspace(), "Models", "LocalAI", "wakeword-venv", "Scripts", "python.exe");
        return File.Exists(workspacePython) ? workspacePython : null;
    }

    private static string? ResolveListenerScript()
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_WAKEWORD_SCRIPT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var outputPath = Path.Combine(
            AppContext.BaseDirectory, "python", "jarvis_runtime", "listen_wake_word.py");
        if (File.Exists(outputPath))
            return outputPath;

        var workspacePath = Path.Combine(
            ResolveWorkspace(), "python", "jarvis_runtime", "listen_wake_word.py");
        return File.Exists(workspacePath) ? workspacePath : null;
    }

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
