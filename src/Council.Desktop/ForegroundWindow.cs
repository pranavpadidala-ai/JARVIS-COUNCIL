using System.Runtime.InteropServices;
using System.Text;

namespace Council.Desktop;

/// <summary>Reads the title of the window in front (used only during focus sessions).</summary>
internal static class ForegroundWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public static string? Title()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return null;
        var text = new StringBuilder(512);
        return GetWindowText(handle, text, text.Capacity) > 0 ? text.ToString() : null;
    }
}

/// <summary>How long since the last keyboard or mouse input.</summary>
internal static class UserIdle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static TimeSpan Duration()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }
}
