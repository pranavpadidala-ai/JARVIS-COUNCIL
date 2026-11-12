using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Runtime.Versioning;
using NAudio.Wave;

namespace Council.App.Voice;

/// <summary>
/// Windows-native, on-demand speech input and output. No microphone is opened
/// until ListenOnceAsync is called; this service never runs a wake-word loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVoiceService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly LocalWhisperSpeechRecognizer _whisper = new();
    private readonly PiperSpeechSynthesizer _piper = new();
    private readonly GeminiCloudSpeechProvider? _cloudSpeech;
    private WaveOutEvent? _activeCloudOutput;
    private readonly object _sync = new();
    private SpeechRecognitionEngine? _activeRecognizer;
    private bool _disposed;

    public WindowsVoiceService(GeminiCloudSpeechProvider? cloudSpeechProvider = null)
    {
        _cloudSpeech = cloudSpeechProvider;
    }

    public async Task<string?> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var localWhisperEnabled =
            !string.Equals(Environment.GetEnvironmentVariable("JARVIS_LOCAL_WHISPER_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);

        if (_cloudSpeech is not null)
        {
            var captureCompleted = false;
            try
            {
                var audio = await _whisper.CaptureAudioOnceAsync(cancellationToken);
                captureCompleted = true;
                if (audio is null) return null;
                try
                {
                    var transcript = await _cloudSpeech.TranscribeAudioAsync(audio, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(transcript)) return transcript;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Console.WriteLine("[Voice] Gemini transcription unavailable; trying local recognition: " + ex.Message);
                }

                if (localWhisperEnabled)
                {
                    try
                    {
                        var localTranscript = await _whisper.TranscribeCapturedAudioAsync(audio, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(localTranscript)) return localTranscript;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Console.WriteLine("[Voice] Local Whisper fallback unavailable: " + ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.WriteLine("[Voice] Gemini audio capture/transcription unavailable: " + ex.Message);
            }

            if (captureCompleted)
                return await ListenWithWindowsSpeechAsync(cancellationToken);
        }

        if (localWhisperEnabled)
        {
            try
            {
                return await _whisper.ListenOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException or TimeoutException or PlatformNotSupportedException)
            {
                Console.WriteLine("[Voice] Local Whisper unavailable; falling back to Windows speech recognition: " + ex.Message);
            }
        }

        return await ListenWithWindowsSpeechAsync(cancellationToken);
    }

    private async Task<string?> ListenWithWindowsSpeechAsync(CancellationToken cancellationToken)
    {
        using var recognizer = new SpeechRecognitionEngine();
        recognizer.LoadGrammar(new DictationGrammar());
        recognizer.SetInputToDefaultAudioDevice();
        lock (_sync)
        {
            if (_activeRecognizer is not null)
                throw new InvalidOperationException("Voice capture is already active.");
            _activeRecognizer = recognizer;
        }
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<RecognizeCompletedEventArgs> handler = (_, args) =>
        {
            if (args.Error is not null) completion.TrySetException(args.Error);
            else if (args.Cancelled && cancellationToken.IsCancellationRequested) completion.TrySetCanceled(cancellationToken);
            else if (args.Cancelled) completion.TrySetResult(null);
            else completion.TrySetResult(args.Result?.Text);
        };
        recognizer.RecognizeCompleted += handler;
        using var registration = cancellationToken.Register(() =>
        {
            try { recognizer.RecognizeAsyncCancel(); }
            catch (InvalidOperationException) { }
        });
        try
        {
            recognizer.RecognizeAsync(RecognizeMode.Single);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        finally
        {
            recognizer.RecognizeCompleted -= handler;
            try { recognizer.RecognizeAsyncCancel(); } catch (InvalidOperationException) { }
            try { recognizer.SetInputToNull(); } catch (InvalidOperationException) { }
            lock (_sync)
            {
                if (ReferenceEquals(_activeRecognizer, recognizer)) _activeRecognizer = null;
            }
        }
    }

    public async Task SpeakAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            return;

        cancellationToken.ThrowIfCancellationRequested();
        if (_cloudSpeech is not null)
        {
            try
            {
                var audio = await _cloudSpeech.SynthesizeSpeechAsync(text, cancellationToken);
                await PlayCloudAudioAsync(audio, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.WriteLine("[Voice] Gemini speech generation unavailable; falling back to local speech: " + ex.Message);
            }
        }
        if (_piper.IsAvailable)
        {
            try
            {
                await _piper.SpeakAsync(text, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
            {
                Console.WriteLine("[Voice] Piper neural speech unavailable; falling back to Windows speech synthesis: " + ex.Message);
            }
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<SpeakCompletedEventArgs> handler = (_, args) =>
        {
            if (args.Error is not null)
                completion.TrySetException(args.Error);
            else if (args.Cancelled)
                completion.TrySetCanceled(cancellationToken);
            else
                completion.TrySetResult();
        };

        _synthesizer.SpeakCompleted += handler;
        using var registration = cancellationToken.Register(() =>
        {
            try { _synthesizer.SpeakAsyncCancelAll(); }
            catch (ObjectDisposedException) { }
        });

        try
        {
            _synthesizer.SpeakAsync(text);
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _synthesizer.SpeakCompleted -= handler;
        }
    }


    private async Task PlayCloudAudioAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(wavBytes);
        using var reader = new WaveFileReader(stream);
        using var output = new WaveOutEvent();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<StoppedEventArgs> handler = (_, args) =>
        {
            if (args.Exception is not null) completion.TrySetException(args.Exception);
            else completion.TrySetResult();
        };
        output.PlaybackStopped += handler;
        lock (_sync) _activeCloudOutput = output;
        using var registration = cancellationToken.Register(() =>
        {
            try { output.Stop(); } catch (InvalidOperationException) { }
        });
        try
        {
            output.Init(reader);
            output.Play();
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            output.PlaybackStopped -= handler;
            lock (_sync)
            {
                if (ReferenceEquals(_activeCloudOutput, output)) _activeCloudOutput = null;
            }
        }
    }

    /// <summary>Stops active speech output and any current recognition session.</summary>
    public void StopAll()
    {
        try { _synthesizer.SpeakAsyncCancelAll(); }
        catch (ObjectDisposedException) { }

        SpeechRecognitionEngine? recognizer;
        WaveOutEvent? cloudOutput;
        lock (_sync)
        {
            recognizer = _activeRecognizer;
            cloudOutput = _activeCloudOutput;
        }
        try { cloudOutput?.Stop(); }
        catch (InvalidOperationException) { }

        if (recognizer is not null)
        {
            try { recognizer.RecognizeAsyncCancel(); }
            catch (InvalidOperationException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAll();
        _synthesizer.Dispose();
        _disposed = true;
    }
}
