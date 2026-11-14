using System.IO;
using System.Threading.Channels;
using NAudio.Wave;
using SherpaOnnx;

namespace Council.Desktop.Voice;

/// <summary>
/// Opt-in, local-only keyword detection. One microphone capture is opened for
/// each listening cycle and is stopped before the command microphone is opened.
/// </summary>
public sealed class WakeWordService
{
    private const int SampleRate = 16_000;
    private const int FeatureDimension = 80;

    private static readonly string[] RequiredFiles =
    [
        "encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
        "decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
        "joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
        "tokens.txt",
        "keywords.txt"
    ];

    private readonly SemaphoreSlim _listenerLock = new(1, 1);

    public static string ModelDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("JARVIS_WAKEWORD_MODEL_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(configured);

            var workspace = Environment.GetEnvironmentVariable("JARVIS_WORKSPACE_ROOT");
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                var repositoryModel = Path.Combine(
                    workspace, "Models", "LocalAI", "WakeWord",
                    "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01");
                if (Directory.Exists(repositoryModel))
                    return repositoryModel;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JARVIS", "models",
                "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01");
        }
    }

    public static bool IsInstalled(out string detail)
    {
        if (!Directory.Exists(ModelDirectory))
        {
            detail = "Wake-word model folder is missing: " + ModelDirectory;
            return false;
        }

        foreach (var file in RequiredFiles)
        {
            var path = Path.Combine(ModelDirectory, file);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                detail = "Required wake-word asset is missing or empty: " + path;
                return false;
            }
        }

        detail = "The local English wake-word model and keyword list are ready.";
        return true;
    }

    /// <summary>
    /// Listens until the configured keyword is detected or the caller cancels.
    /// The microphone and inference objects are released before this method returns.
    /// </summary>
    public async Task<string> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Wake-word microphone capture is available on Windows only.");

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInstalled(out var detail))
            throw new InvalidOperationException(detail + " Run scripts/Install-WakeWord.ps1 first.");

        await _listenerLock.WaitAsync(cancellationToken);
        try
        {
            return await ListenCoreAsync(cancellationToken);
        }
        finally
        {
            _listenerLock.Release();
        }
    }

    private static async Task<string> ListenCoreAsync(CancellationToken cancellationToken)
    {
        var modelDirectory = ModelDirectory;
        var config = new KeywordSpotterConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = FeatureDimension;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDirectory, RequiredFiles[0]);
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDirectory, RequiredFiles[1]);
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDirectory, RequiredFiles[2]);
        config.ModelConfig.Tokens = Path.Combine(modelDirectory, "tokens.txt");
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Debug = 0;
        config.KeywordsFile = Path.Combine(modelDirectory, "keywords.txt");
        config.KeywordsScore = 1.5f;
        config.KeywordsThreshold = 0.25f;

        using var spotter = new KeywordSpotter(config);
        var stream = spotter.CreateStream();

        var channel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(20)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        using var input = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 100,
            NumberOfBuffers = 4
        };

        var recordingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? captureError = null;
        var droppedChunks = 0;
        var recordingStarted = false;
        var detectedKeyword = string.Empty;

        input.DataAvailable += (_, args) =>
        {
            var sampleCount = args.BytesRecorded / sizeof(short);
            if (sampleCount <= 0)
                return;

            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var pcm = BitConverter.ToInt16(args.Buffer, i * sizeof(short));
                samples[i] = pcm / 32768.0f;
            }

            if (!channel.Writer.TryWrite(samples))
                Interlocked.Exchange(ref droppedChunks, 1);
        };

        input.RecordingStopped += (_, args) =>
        {
            captureError = args.Exception;
            channel.Writer.TryComplete(args.Exception);
            recordingStopped.TrySetResult();
        };

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try { input.StopRecording(); }
            catch (InvalidOperationException) { }
        });

        try
        {
            input.StartRecording();
            recordingStarted = true;

            await foreach (var samples in channel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // If the bounded audio queue overflowed, discard the decoder's
                // previous context rather than joining non-contiguous audio.
                if (Interlocked.Exchange(ref droppedChunks, 0) != 0)
                    spotter.Reset(stream);

                stream.AcceptWaveform(SampleRate, samples);
                while (spotter.IsReady(stream))
                {
                    spotter.Decode(stream);
                    var result = spotter.GetResult(stream).Keyword?.Trim();
                    if (string.IsNullOrWhiteSpace(result))
                        continue;

                    detectedKeyword = result;
                    try { input.StopRecording(); }
                    catch (InvalidOperationException) { }
                    break;
                }

                if (detectedKeyword.Length > 0)
                    break;
            }
        }
        finally
        {
            if (recordingStarted)
            {
                try { input.StopRecording(); }
                catch (InvalidOperationException) { }

                try { await recordingStopped.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { }
            }

            channel.Writer.TryComplete();
            stream.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (captureError is not null)
            throw new InvalidOperationException(
                "The wake-word microphone stopped unexpectedly: " + captureError.Message,
                captureError);

        if (string.IsNullOrWhiteSpace(detectedKeyword))
            throw new InvalidOperationException("The wake-word microphone stopped without a keyword result.");

        return detectedKeyword;
    }
}
