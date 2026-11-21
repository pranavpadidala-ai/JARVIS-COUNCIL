namespace Council.App.Safety;

/// <summary>
/// Master emergency stop (spec section 9). Halting cancels every request linked to
/// <see cref="Token"/> and blocks all tools until the user resumes JARVIS.
/// </summary>
public sealed class EmergencyStopSwitch
{
    /// <summary>The switch shared by the desktop app, chat windows and the assistant runtime.</summary>
    public static EmergencyStopSwitch Default { get; } = new();

    private readonly object _gate = new();
    private CancellationTokenSource _source = new();

    public bool IsHalted { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset? HaltedAt { get; private set; }

    /// <summary>Raised after every halt or resume. Handlers may run on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Cancelled when JARVIS is halted. Link request cancellation sources to it.</summary>
    public CancellationToken Token
    {
        get { lock (_gate) return _source.Token; }
    }

    public const string BlockedMessage =
        "JARVIS is in emergency stop. No tools will run until you resume (tray menu \"Resume JARVIS\" or /resume).";

    public void Halt(string reason)
    {
        CancellationTokenSource toCancel;
        lock (_gate)
        {
            if (IsHalted) return;
            IsHalted = true;
            Reason = string.IsNullOrWhiteSpace(reason) ? "Emergency stop" : reason.Trim();
            HaltedAt = DateTimeOffset.Now;
            toCancel = _source;
        }
        try { toCancel.Cancel(); }
        catch (AggregateException) { } // a cancellation callback failing must not stop the halt
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!IsHalted) return;
            _source.Dispose();
            _source = new CancellationTokenSource();
            IsHalted = false;
            Reason = null;
            HaltedAt = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
