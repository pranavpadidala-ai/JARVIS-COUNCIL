using System.Diagnostics;
using NAudio.Wave;

namespace Council.App.Voice;

/// <summary>
/// Optional local neural speech output using Piper. A short-lived Piper process
/// synthesizes each reply, and NAudio plays the resulting WAV without cloud calls.
/// </summary>
public sealed class PiperSpeechSynthesizer
{
    private readonly string _pythonPath;
    private readonly string _voicesDirectory;
    private readonly string _defaultVoice;

    public PiperSpeechSynthesizer(string? pythonPath = null, string? voicesDirectory = null, string? defaultVoice = null)
    {
        var root = LocalModelPaths.ResolveRoot();
        _pythonPath = pythonPath
            ?? Environment.GetEnvironmentVariable("JARVIS_PIPER_PYTHON")
            ?? Path.Combine(root, "piper-venv", "Scripts", "python.exe");
        _voicesDirectory = voicesDirectory
            ?? Environment.GetEnvironmentVariable("JARVIS_PIPER_VOICES")
            ?? Path.Combine(root, "voices");
        _defaultVoice = defaultVoice
            ?? Environment.GetEnvironmentVariable("JARVIS_PIPER_VOICE")
            ?? "en_US-lessac-medium";
    }

    public bool IsAvailable =>
        File.Exists(_pythonPath) &&
        File.Exists(Path.Combine(_voicesDirectory, _defaultVoice + ".onnx")) &&
        File.Exists(Path.Combine(_voicesDirectory, _defaultVoice + ".onnx.json"));

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        var voice = SelectVoice(text);
        var modelPath = Path.Combine(_voicesDirectory, voice + ".onnx");
        var configPath = modelPath + ".json";
        if (!File.Exists(modelPath) || !File.Exists(configPath))
            throw new InvalidOperationException($"Piper voice '{voice}' is not installed. Run scripts/Install-LocalModels.ps1.");

        var outputPath = Path.Combine(Path.GetTempPath(), "jarvis-speech-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var start = new ProcessStartInfo(_pythonPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };
            start.ArgumentList.Add("-m");
            start.ArgumentList.Add("piper");
            start.ArgumentList.Add("--model");
            start.ArgumentList.Add(modelPath);
            start.ArgumentList.Add("--output_file");
            start.ArgumentList.Add(outputPath);
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(text);

            using var process = new Process { StartInfo = start };
            if (!process.Start())
                throw new InvalidOperationException("Could not start the local Piper speech process.");

            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                throw;
            }
            var error = await errorTask;
            if (process.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException("Piper synthesis failed: " + error[..Math.Min(error.Length, 1_000)]);

            using var audio = new AudioFileReader(outputPath);
            using var output = new WaveOutEvent { DesiredLatency = 120 };
            var playback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                    playback.TrySetException(args.Exception);
                else
                    playback.TrySetResult();
            };
            using var registration = cancellationToken.Register(() =>
            {
                try { output.Stop(); }
                catch (InvalidOperationException) { }
            });
            output.Init(audio);
            output.Play();
            await playback.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string SelectVoice(string text)
    {
        if (text.Any(character => character is >= '\u0C00' and <= '\u0C7F'))
            return "te_IN-maya-medium";
        if (text.Any(character => character is >= '\u0900' and <= '\u097F'))
            return "hi_IN-pratham-medium";
        return _defaultVoice;
    }
}
