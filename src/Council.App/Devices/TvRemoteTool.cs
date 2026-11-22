using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Council.App.Voice;
using Council.Execution.Tools;

namespace Council.App.Devices;

/// <summary>
/// Controls an Android TV device on the home Wi-Fi (the owner's Jio set-top box JHSC200v3) through
/// python/jarvis_runtime/tv_remote.py. Pairing is a two-step chat flow: pair_start makes the TV show a code,
/// pair_finish sends the code the user reads out. Only private-network addresses are accepted.
/// </summary>
public sealed class TvRemoteTool : ITool
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["power"] = "POWER", ["home"] = "HOME", ["back"] = "BACK", ["up"] = "DPAD_UP", ["down"] = "DPAD_DOWN",
        ["left"] = "DPAD_LEFT", ["right"] = "DPAD_RIGHT", ["ok"] = "DPAD_CENTER", ["select"] = "DPAD_CENTER",
        ["volume_up"] = "VOLUME_UP", ["volume_down"] = "VOLUME_DOWN", ["mute"] = "VOLUME_MUTE",
        ["play_pause"] = "MEDIA_PLAY_PAUSE", ["play"] = "MEDIA_PLAY", ["pause"] = "MEDIA_PAUSE", ["stop"] = "MEDIA_STOP",
        ["next"] = "MEDIA_NEXT", ["previous"] = "MEDIA_PREVIOUS", ["rewind"] = "MEDIA_REWIND", ["fast_forward"] = "MEDIA_FAST_FORWARD",
        ["channel_up"] = "CHANNEL_UP", ["channel_down"] = "CHANNEL_DOWN", ["guide"] = "GUIDE", ["menu"] = "MENU",
        ["search"] = "SEARCH", ["settings"] = "SETTINGS"
    };

    private static readonly Dictionary<string, string> Apps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://www.youtube.com",
        ["netflix"] = "https://www.netflix.com/title",
        ["prime video"] = "https://app.primevideo.com",
        ["jiotv"] = "com.jio.jioplay.tv",
        ["jiocinema"] = "com.jio.media.stb.ondemand",
        ["hotstar"] = "in.startv.hotstar"
    };

    private static Process? _pairing;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<string>> _run;
    private readonly string _settingsPath;

    public TvRemoteTool(Func<IReadOnlyList<string>, CancellationToken, Task<string>>? run = null, string? settingsPath = null)
    {
        _run = run ?? RunScriptAsync;
        _settingsPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "tv", "host.txt");
    }

    public string Name => "tv_remote";

    public string Description =>
        "Control the TV set-top box (Jio Android TV box) on the home Wi-Fi. Arguments: action = discover | pair_start | pair_finish | key | open_app | type | status. " +
        "discover: finds the box. pair_start: host (from discover); the TV then shows a code; ask the user for it and call pair_finish with code. " +
        "key: key = power|home|back|up|down|left|right|ok|volume_up|volume_down|mute|play_pause|next|previous|channel_up|channel_down|search|settings. " +
        "open_app: app = youtube|netflix|prime video|jiotv|jiocinema|hotstar (or a package name). type: text for a search box. " +
        "After pairing, host is remembered.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        var action = Arg("action").ToLowerInvariant();
        try
        {
            if (action == "discover") return Summarise(await _run(new[] { "discover" }, cancellationToken));
            if (action == "pair_finish") return await FinishPairingAsync(Arg("code"));

            var host = Arg("host").Length > 0 ? Arg("host") : SavedHost();
            if (host is null) return new ToolResult(false, "Which TV? Run discover first, then pair_start with its host.");
            if (!IsPrivateAddress(host)) return new ToolResult(false, "Only TVs on the home network (private IP addresses) are allowed.");

            switch (action)
            {
                case "pair_start":
                    return await StartPairingAsync(host);
                case "key":
                    if (!Keys.TryGetValue(Arg("key").Replace(' ', '_'), out var key)) return new ToolResult(false, "Unknown key. " + string.Join(", ", Keys.Keys));
                    return Summarise(await _run(new[] { "key", host, key }, cancellationToken));
                case "open_app":
                    var app = Apps.TryGetValue(Arg("app"), out var known) ? known : Arg("app");
                    if (app.Length == 0 || app.Any(char.IsWhiteSpace)) return new ToolResult(false, "app must be one of " + string.Join(", ", Apps.Keys) + " or a package name.");
                    return Summarise(await _run(new[] { "launch", host, app }, cancellationToken));
                case "type":
                    var text = Arg("text");
                    if (text.Length is 0 or > 200) return new ToolResult(false, "text is required (up to 200 characters).");
                    return Summarise(await _run(new[] { "text", host, text }, cancellationToken));
                case "status":
                    return Summarise(await _run(new[] { "status", host }, cancellationToken));
                default:
                    return new ToolResult(false, "action must be discover, pair_start, pair_finish, key, open_app, type or status.");
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new ToolResult(false, "TV control failed: " + ex.Message);
        }
    }

    public static bool IsPrivateAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private ToolResult Summarise(string output)
    {
        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'));
        if (line is null) return new ToolResult(false, "The TV helper returned nothing.");
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var o) && o.GetBoolean();
        if (!ok) return new ToolResult(false, root.TryGetProperty("error", out var e) ? e.GetString() ?? "TV command failed." : "TV command failed.");
        if (root.TryGetProperty("devices", out var devices))
        {
            var list = devices.EnumerateArray().Select(d => $"{d.GetProperty("name").GetString()} at {d.GetProperty("host").GetString()}").ToList();
            return new ToolResult(true, list.Count == 0
                ? "No Android TV found. Make sure the Jio box is on and on the same Wi-Fi."
                : "Found: " + string.Join("; ", list) + ". Use pair_start with the host.");
        }
        return new ToolResult(true, line);
    }

    private async Task<ToolResult> StartPairingAsync(string host)
    {
        _pairing?.Kill(entireProcessTree: true);
        var process = StartScript(new[] { "pair", host }, redirectInput: true);
        var first = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (first is null || !first.Contains("code_required"))
        {
            process.Kill(entireProcessTree: true);
            return first is null ? new ToolResult(false, "The TV did not respond.") : Summarise(first);
        }
        _pairing = process;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await File.WriteAllTextAsync(_settingsPath, host);
        return new ToolResult(true, "The TV is showing a pairing code now. Ask the user to read it out, then call pair_finish with code.");
    }

    private static async Task<ToolResult> FinishPairingAsync(string code)
    {
        var process = _pairing;
        if (process is null || process.HasExited) return new ToolResult(false, "No pairing in progress. Run pair_start again.");
        if (code.Length is < 4 or > 10) return new ToolResult(false, "code should be the 6 characters shown on the TV.");
        await process.StandardInput.WriteLineAsync(code);
        await process.StandardInput.FlushAsync();
        var rest = await process.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(20));
        _pairing = null;
        return rest.Contains("\"paired\"")
            ? new ToolResult(true, "Paired with the TV. You can now say things like 'open YouTube on TV' or 'TV volume up'.")
            : new ToolResult(false, "Pairing failed. Check the code and try pair_start again.");
    }

    private string? SavedHost() => File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath).Trim() : null;

    private static Process StartScript(IReadOnlyList<string> args, bool redirectInput = false)
    {
        var python = Environment.GetEnvironmentVariable("JARVIS_TV_PYTHON") ?? Path.Combine(LocalModelPaths.ResolveRoot(), "tv-venv", "Scripts", "python.exe");
        if (!File.Exists(python)) throw new FileNotFoundException("TV control is not installed yet (missing " + python + ").");
        var script = FindScript() ?? throw new FileNotFoundException("python\\jarvis_runtime\\tv_remote.py was not found.");
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = redirectInput, CreateNoWindow = true
        };
        start.ArgumentList.Add(script);
        foreach (var a in args) start.ArgumentList.Add(a);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the TV helper.");
    }

    private static async Task<string> RunScriptAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        using var process = StartScript(args);
        var output = process.StandardOutput.ReadToEndAsync(token);
        _ = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new IOException("The TV did not answer in time.");
        }
        return await output;
    }

    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "python", "jarvis_runtime", "tv_remote.py");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
