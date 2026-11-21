using Council.Execution.Tools;

namespace Council.App.Transfer;

/// <summary>Chat control for Wi-Fi file sharing with a phone.</summary>
public sealed class FileShareTool : ITool
{
    private static readonly WifiShareServer SharedServer = new();
    private readonly WifiShareServer _server;

    public FileShareTool(WifiShareServer? server = null) => _server = server ?? SharedServer;

    public string Name => "file_share";

    public string Description =>
        "Send files between this laptop and the user's phone over the same Wi-Fi. Arguments: action = start | share | stop | status. " +
        "start: optional minutes (5-180, default 30); returns a link to open on the phone's browser (upload and download). " +
        "share: path of a laptop file to offer to the phone (starts sharing if needed). Files from the phone are saved in " +
        "Downloads\\JARVIS-Share. The first time, Windows Firewall may ask to allow JARVIS on private networks: the user must click Allow. " +
        "Only works on a private/home network, not public Wi-Fi.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        try
        {
            switch (Arg("action").ToLowerInvariant())
            {
                case "start":
                    var minutes = int.TryParse(Arg("minutes"), out var m) ? Math.Clamp(m, 5, 180) : 30;
                    return new ToolResult(true, Started(await _server.StartAsync(TimeSpan.FromMinutes(minutes))));
                case "share":
                    var name = _server.Share(Arg("path"));
                    var links = _server.Running ? _server.Links() : await _server.StartAsync(TimeSpan.FromMinutes(30));
                    return new ToolResult(true, $"Ready to download on the phone: {name}\n" + Started(links));
                case "stop":
                    await _server.StopAsync();
                    return new ToolResult(true, "File sharing stopped.");
                case "status":
                    return new ToolResult(true, _server.Running
                        ? $"Sharing until {_server.StopsAt:HH:mm}. Open on the phone: {string.Join(" or ", _server.Links())}"
                        : "File sharing is off.");
                default:
                    return new ToolResult(false, "action must be start, share, stop or status.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                       or System.Net.Sockets.SocketException)
        {
            return new ToolResult(false, "File sharing failed: " + ex.Message);
        }
    }

    private string Started(IReadOnlyList<string> links) =>
        $"On the phone (same Wi-Fi), open: {string.Join(" or ", links)}\n" +
        $"Files from the phone go to {_server.Folder}. Sharing stops at {_server.StopsAt:HH:mm}. " +
        "If Windows asks about the firewall, click Allow (private networks only).";
}
