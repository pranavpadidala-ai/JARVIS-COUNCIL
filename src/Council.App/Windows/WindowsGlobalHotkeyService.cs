using System.Runtime.InteropServices;

namespace Council.App.Windows;

/// <summary>
/// Registers Ctrl+Alt+J as a system-wide shortcut that restores and focuses
/// the current JARVIS console window. It does not start microphone capture.
/// </summary>
public sealed class WindowsGlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4A52;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;
    private const int SwRestore = 9;

    private readonly Thread _messageThread;
    private readonly ManualResetEventSlim _startup = new(false);
    private uint _threadId;
    private Exception? _startupError;
    private bool _registered;
    private bool _disposed;

    public WindowsGlobalHotkeyService()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The global activation shortcut is available on Windows only.");

        _messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "JARVIS global hotkey"
        };
        _messageThread.Start();

        if (!_startup.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("JARVIS could not initialize its global activation shortcut.");

        if (_startupError is not null)
            throw new InvalidOperationException("JARVIS could not register Ctrl+Alt+J. The shortcut may already be in use.", _startupError);

        if (!_registered)
            throw new InvalidOperationException("JARVIS could not register Ctrl+Alt+J. The shortcut may already be in use.");
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);

        try
        {
            _registered = RegisterHotKey(IntPtr.Zero, HotkeyId, ModControl | ModAlt, (uint)'J');
        }
        catch (Exception ex)
        {
            _startupError = ex;
        }
        finally
        {
            _startup.Set();
        }

        if (!_registered)
            return;

        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message != WmHotkey || message.WParam.ToInt32() != HotkeyId)
                    continue;

                var consoleWindow = GetConsoleWindow();
                if (consoleWindow == IntPtr.Zero)
                    continue;

                _ = ShowWindow(consoleWindow, SwRestore);
                _ = SetForegroundWindow(consoleWindow);
            }
        }
        finally
        {
            _ = UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_threadId != 0)
            _ = PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

        if (Thread.CurrentThread != _messageThread)
            _messageThread.Join(TimeSpan.FromSeconds(2));

        _startup.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr HWnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr hWnd, uint minFilter, uint maxFilter, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
