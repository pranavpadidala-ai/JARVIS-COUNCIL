using System.Diagnostics;
using System.Net.Sockets;

namespace Council.App.Chat;

/// <summary>
/// Starts the local OmniRoute gateway when JARVIS opens with OmniRoute selected, and stops it when
/// JARVIS exits, so its ~500 MB is used only while JARVIS runs (OmniRoute's own autostart would keep
/// it running from boot). An OmniRoute that was already running is left alone.
/// </summary>
public sealed class OmniRouteLauncher : IDisposable
{
    private readonly string? _launcherPath;
    private readonly Uri _baseUri;
    private Process? _started;

    public OmniRouteLauncher(string? launcherPath, string baseUrl)
    {
        _launcherPath = launcherPath;
        _baseUri = new Uri(baseUrl);
    }

    /// <summary>True when JARVIS started OmniRoute itself (and will stop it on exit).</summary>
    public bool StartedByJarvis => _started is { HasExited: false };

    /// <summary>
    /// Starts OmniRoute in a hidden window if it is selected, local and not already listening.
    /// Returns a short status for the tray, or null when nothing needed doing.
    /// </summary>
    public string? EnsureRunning(string? selectedProvider)
    {
        if (!string.Equals(selectedProvider, "omniroute", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!_baseUri.IsLoopback)
            return null; // a remote gateway is someone else's to run
        if (IsListening(_baseUri.Host, _baseUri.Port))
            return null;
        if (string.IsNullOrWhiteSpace(_launcherPath) || !File.Exists(_launcherPath))
            return "OmniRoute is not running and its launcher was not found, so JARVIS will use the local model. " +
                   "Set JARVIS_OMNIROUTE_LAUNCHER to the Start-OmniRoute.cmd path.";

        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_launcherPath)!
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add(_launcherPath);
        _started = Process.Start(start);
        return _started is null
            ? "Could not start OmniRoute; JARVIS will use the local model."
            : "Starting the free cloud AI (OmniRoute). Until it is ready, JARVIS answers with the local model.";
    }

    /// <summary>Stops OmniRoute only if JARVIS started it.</summary>
    public void Stop()
    {
        if (_started is null) return;
        try
        {
            if (!_started.HasExited)
                _started.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        _started.Dispose();
        _started = null;
    }

    public void Dispose() => Stop();

    public static bool IsListening(string host, int port)
    {
        // "localhost" can resolve to both ::1 and 127.0.0.1, and a server may listen on only one of them.
        System.Net.IPAddress[] addresses;
        try { addresses = System.Net.Dns.GetHostAddresses(host); }
        catch (SocketException) { return false; }

        foreach (var address in addresses)
        {
            try
            {
                using var client = new TcpClient(address.AddressFamily);
                if (client.ConnectAsync(address, port).Wait(TimeSpan.FromSeconds(1)) && client.Connected)
                    return true;
            }
            catch (Exception ex) when (ex is SocketException or AggregateException) { }
        }
        return false;
    }

    /// <summary>
    /// JARVIS_OMNIROUTE_LAUNCHER, else "JARVIS-tools\Start-OmniRoute.cmd" next to the folder that holds
    /// JARVIS.Council.sln (found by walking up from <paramref name="startDirectory"/>).
    /// </summary>
    public static string? FindLauncher(string startDirectory)
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_OMNIROUTE_LAUNCHER");
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "JARVIS.Council.sln"))) continue;
            var candidate = directory.Parent is null ? null : Path.Combine(directory.Parent.FullName, "JARVIS-tools", "Start-OmniRoute.cmd");
            return candidate is not null && File.Exists(candidate) ? candidate : null;
        }
        return null;
    }
}
