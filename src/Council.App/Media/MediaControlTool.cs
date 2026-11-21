using System.Diagnostics;
using System.Runtime.InteropServices;
using Council.Execution.Tools;

namespace Council.App.Media;

/// <summary>
/// Music and media control. Sends the standard Windows media keys (they work with Spotify, YouTube
/// in the browser, VLC, Windows Media Player and others), plays a song from the user's Music folder,
/// or opens a YouTube Music search. It never downloads anything.
/// </summary>
public sealed class MediaControlTool : ITool
{
    private static readonly string[] AudioExtensions = { ".mp3", ".m4a", ".wav", ".flac", ".wma", ".aac", ".ogg", ".opus" };

    private static readonly IReadOnlyDictionary<string, byte> Keys = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
    {
        ["play_pause"] = 0xB3, ["pause"] = 0xB3, ["resume"] = 0xB3,
        ["next"] = 0xB0, ["previous"] = 0xB1, ["stop"] = 0xB2,
        ["volume_up"] = 0xAF, ["volume_down"] = 0xAE, ["mute"] = 0xAD
    };

    private readonly Action<byte> _pressKey;
    private readonly Func<string, bool> _open;
    private readonly string _musicFolder;

    public MediaControlTool(Action<byte>? pressKey = null, Func<string, bool>? open = null, string? musicFolder = null)
    {
        _pressKey = pressKey ?? PressKey;
        _open = open ?? OpenWithShell;
        _musicFolder = musicFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    }

    public string Name => "media_control";

    public string Description =>
        "Control music and media on this laptop. Arguments: action = play_pause | pause | resume | next | previous | stop | " +
        "volume_up | volume_down | mute | play_song | search_online. volume_up/volume_down take optional steps (1-25, each about 2%). " +
        "play_song: query (song or artist); plays the best match from the user's Music folder. " +
        "search_online: query; opens a YouTube Music search in the browser. Media keys act on whatever app is playing.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return Task.FromResult(new ToolResult(false, "Media control works on Windows only."));
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        var action = (args.TryGetValue("action", out var a) ? a : "").Trim().ToLowerInvariant();
        var query = (args.TryGetValue("query", out var q) ? q : "").Trim();

        try
        {
            if (Keys.TryGetValue(action, out var key))
            {
                var steps = 1;
                if (action.StartsWith("volume_") && args.TryGetValue("steps", out var s) && int.TryParse(s, out var n))
                    steps = Math.Clamp(n, 1, 25);
                for (var i = 0; i < steps; i++) _pressKey(key);
                return Task.FromResult(new ToolResult(true, Describe(action, steps)));
            }

            return Task.FromResult(action switch
            {
                "play_song" => PlaySong(query, cancellationToken),
                "search_online" => SearchOnline(query),
                _ => new ToolResult(false, "Unknown action. Use play_pause, next, previous, stop, volume_up, volume_down, mute, play_song or search_online.")
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new ToolResult(false, "Media control failed: " + ex.Message));
        }
    }

    private ToolResult PlaySong(string query, CancellationToken token)
    {
        if (query.Length == 0) return new ToolResult(false, "query (song or artist) is required.");
        var match = FindSong(_musicFolder, query, token);
        if (match is null)
            return new ToolResult(false,
                $"No song matching '{query}' in {_musicFolder}. Use search_online to find it on YouTube Music instead.");
        return _open(match)
            ? new ToolResult(true, "Playing " + Path.GetFileNameWithoutExtension(match))
            : new ToolResult(false, "Windows could not open " + match);
    }

    private ToolResult SearchOnline(string query)
    {
        if (query.Length == 0) return new ToolResult(false, "query is required.");
        var url = "https://music.youtube.com/search?q=" + Uri.EscapeDataString(query);
        return _open(url)
            ? new ToolResult(true, $"Opened YouTube Music search for '{query}'. Pick a result to play it.")
            : new ToolResult(false, "Could not open the browser.");
    }

    /// <summary>Best match by how many query words appear in the file path; ties go to the shorter name.</summary>
    public static string? FindSong(string folder, string query, CancellationToken token = default)
    {
        if (!Directory.Exists(folder)) return null;
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        string? best = null; var bestScore = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            token.ThrowIfCancellationRequested();
            if (!AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            var name = Path.GetRelativePath(folder, file).ToLowerInvariant();
            var score = words.Count(name.Contains);
            if (score > bestScore || (score == bestScore && score > 0 && best is not null && file.Length < best.Length))
            {
                best = file; bestScore = score;
            }
        }
        return bestScore * 2 >= words.Length ? best : null; // at least half the words must match
    }

    private static string Describe(string action, int steps) => action switch
    {
        "next" => "Skipped to the next track.",
        "previous" => "Went back to the previous track.",
        "stop" => "Stopped playback.",
        "mute" => "Toggled mute.",
        "volume_up" => $"Volume up ({steps} step{(steps == 1 ? "" : "s")}).",
        "volume_down" => $"Volume down ({steps} step{(steps == 1 ? "" : "s")}).",
        _ => "Toggled play/pause."
    };

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    private static void PressKey(byte vk)
    {
        const uint extendedKey = 0x1, keyUp = 0x2;
        keybd_event(vk, 0, extendedKey, UIntPtr.Zero);
        keybd_event(vk, 0, extendedKey | keyUp, UIntPtr.Zero);
    }

    private static bool OpenWithShell(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }) is not null;
}
