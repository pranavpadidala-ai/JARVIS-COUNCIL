using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Runtime.Versioning;

namespace Council.App.Voice;

/// <summary>
/// Windows-native, on-demand speech input and output. No microphone is opened
/// until ListenOnceAsync is called; this service never runs a wake-word loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVoiceService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly object _sync = new();
    private SpeechRecognitionEngine? _activeRecognizer;
    private bool _disposed;

    public async Task<string?> ListenOnceAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var recognizer = new SpeechRecognitionEngine();
        recognizer.LoadGrammar(new DictationGrammar());
        recognizer.SetInputToDefaultAudioDevice();

        lock (_sync)
        {
            if (_activeRecognizer is not null)
                throw new InvalidOperationException("Voice capture is already active.");
            _activeRecognizer = recognizer;
        }

        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<RecognizeCompletedEventArgs> handler = (_, args) =>
        {
            if (args.Error is not null)
                completion.TrySetException(args.Error);
            else if (args.Cancelled)
                completion.TrySetResult(null);
            else
                completion.TrySetResult(args.Result?.Text);
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
            return await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(20), cancellationToken);
        }
        finally
        {
            recognizer.RecognizeCompleted -= handler;
            try { recognizer.RecognizeAsyncCancel(); }
            catch (InvalidOperationException) { }
            recognizer.SetInputToNull();
            lock (_sync)
            {
                if (ReferenceEquals(_activeRecognizer, recognizer))
                    _activeRecognizer = null;
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

    /// <summary>Stops active speech output and any current recognition session.</summary>
    public void StopAll()
    {
        try { _synthesizer.SpeakAsyncCancelAll(); }
        catch (ObjectDisposedException) { }

        SpeechRecognitionEngine? recognizer;
        lock (_sync)
            recognizer = _activeRecognizer;

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
