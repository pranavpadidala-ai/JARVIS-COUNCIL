using System.Diagnostics;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace Council.App.Voice;

/// <summary>
/// On-demand multilingual local speech recognition. The microphone is opened
/// only during ListenOnceAsync; the Whisper model is disposed after each capture.
/// </summary>
public sealed class LocalWhisperSpeechRecognizer
{
    private static readonly SemaphoreSlim ModelDownloadLock = new(1, 1);
    private readonly string _modelPath;

    public LocalWhisperSpeechRecognizer(string? modelPath = null)
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_WHISPER_MODEL_PATH");
        _modelPath = modelPath ?? configured ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JARVIS", "models", "ggml-small.bin");
    }

    public async Task<string?> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Local microphone capture is currently supported on Windows only.");

        await EnsureModelAsync(cancellationToken);
        var audioPath = Path.Combine(Path.GetTempPath(), "jarvis-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var captured = await CaptureUntilSilenceAsync(audioPath, cancellationToken);
            if (!captured)
                return null;

            using var factory = WhisperFactory.FromPath(_modelPath);
            using var processor = factory.CreateBuilder().WithLanguage("auto").Build();
            await using var audio = File.OpenRead(audioPath);
            var transcript = new System.Text.StringBuilder();
            await foreach (var segment in processor.ProcessAsync(audio).WithCancellation(cancellationToken))
                transcript.Append(segment.Text);
            var result = transcript.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
        finally
        {
            try { if (File.Exists(audioPath)) File.Delete(audioPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task EnsureModelAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_modelPath) && new FileInfo(_modelPath).Length > 100_000_000)
            return;

        await ModelDownloadLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_modelPath) && new FileInfo(_modelPath).Length > 100_000_000)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
            var temporaryPath = _modelPath + ".download";
            try
            {
                Console.WriteLine("[Voice] Downloading Whisper small multilingual model (about 466 MB)...");
                await using var model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Small);
                await using (var output = File.Create(temporaryPath))
                    await model.CopyToAsync(output, cancellationToken);
                File.Move(temporaryPath, _modelPath, overwrite: true);
                Console.WriteLine("[Voice] Whisper model ready.");
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
            }
        }
        finally
        {
            ModelDownloadLock.Release();
        }
    }

    private static async Task<bool> CaptureUntilSilenceAsync(string path, CancellationToken cancellationToken)
    {
        using var input = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16_000, 16, 1),
            BufferMilliseconds = 100,
            NumberOfBuffers = 3
        };
        using var writer = new WaveFileWriter(path, input.WaveFormat);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var speechDetected = false;
        var speechMilliseconds = 0;
        var silenceMilliseconds = 0;
        var noSpeechMilliseconds = 0;
        Exception? captureError = null;

        input.DataAvailable += (_, args) =>
        {
            writer.Write(args.Buffer, 0, args.BytesRecorded);
            long energy = 0;
            var samples = args.BytesRecorded / 2;
            for (var i = 0; i < samples; i++)
            {
                var sample = BitConverter.ToInt16(args.Buffer, i * 2);
                energy += (long)sample * sample;
            }

            var rms = samples == 0 ? 0 : Math.Sqrt((double)energy / samples);
            if (rms >= 420)
            {
                speechDetected = true;
                speechMilliseconds += input.BufferMilliseconds;
                silenceMilliseconds = 0;
            }
            else if (speechDetected)
            {
                silenceMilliseconds += input.BufferMilliseconds;
            }
            else
            {
                noSpeechMilliseconds += input.BufferMilliseconds;
            }

            if ((speechDetected && speechMilliseconds >= 500 && silenceMilliseconds >= 1_300) ||
                (!speechDetected && noSpeechMilliseconds >= 4_000))
            {
                try { input.StopRecording(); }
                catch (InvalidOperationException) { }
            }
        };
        input.RecordingStopped += (_, args) =>
        {
            captureError = args.Exception;
            stopped.TrySetResult();
        };

        using var registration = cancellationToken.Register(() =>
        {
            try { input.StopRecording(); }
            catch (InvalidOperationException) { }
        });

        try
        {
            input.StartRecording();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(22), cancellationToken);
        }
        finally
        {
            try { input.StopRecording(); }
            catch (InvalidOperationException) { }
            if (!stopped.Task.IsCompleted)
            {
                try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { }
            }
        }

        if (captureError is not null)
            throw new InvalidOperationException("Microphone capture failed: " + captureError.Message, captureError);
        cancellationToken.ThrowIfCancellationRequested();
        return speechDetected && speechMilliseconds >= 300;
    }
}
