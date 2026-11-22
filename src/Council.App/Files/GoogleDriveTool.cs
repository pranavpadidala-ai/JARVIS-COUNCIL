using System.Diagnostics;
using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Files;

/// <summary>
/// Google Drive through rclone (free, open source), using the "gdrive:" remote the user connects once with
/// scripts/Connect-GoogleDrive.ps1. Supports list, search, download and upload. It never deletes, moves or
/// overwrites anything in Drive, and downloads go to Downloads\JARVIS-Drive without replacing files.
/// </summary>
public sealed class GoogleDriveTool : ITool
{
    private const string Remote = "gdrive:";
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<(int Exit, string Output)>> _rclone;
    private readonly string _downloadFolder;

    public GoogleDriveTool(Func<IReadOnlyList<string>, CancellationToken, Task<(int, string)>>? rclone = null, string? downloadFolder = null)
    {
        _rclone = rclone ?? RunRcloneAsync;
        _downloadFolder = downloadFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "JARVIS-Drive");
    }

    public string Name => "google_drive";

    public string Description =>
        "The user's Google Drive. Arguments: action = list | search | download | upload | space. " +
        "list: optional folder (Drive path, empty for the top level). search: query (part of a file name). " +
        "download: path (Drive file path); saved to Downloads\\JARVIS-Drive. upload: local path and optional folder in Drive. " +
        "Never deletes or overwrites Drive files. If Drive is not connected, tell the user to run scripts\\Connect-GoogleDrive.ps1 and sign in themselves.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        try
        {
            switch (Arg("action").ToLowerInvariant())
            {
                case "list":
                {
                    var folder = DrivePath(Arg("folder"));
                    var (exit, output) = await _rclone(new[] { "lsjson", Remote + folder, "--max-depth", "1", "--no-mimetype" }, cancellationToken);
                    return exit != 0 ? Failed(output) : new ToolResult(true, Format(output, folder, 100));
                }
                case "search":
                {
                    var query = Arg("query");
                    if (query.Length is 0 or > 100) return new ToolResult(false, "query is required.");
                    var (exit, output) = await _rclone(new[] { "lsjson", Remote, "-R", "--files-only", "--no-mimetype", "--ignore-case",
                        "--include", "*" + EscapeGlob(query) + "*", "--max-depth", "8" }, cancellationToken);
                    return exit != 0 ? Failed(output) : new ToolResult(true, Format(output, "", 50, includePath: true));
                }
                case "download":
                {
                    var path = DrivePath(Arg("path"));
                    if (path.Length == 0) return new ToolResult(false, "path (the Drive file) is required.");
                    Directory.CreateDirectory(_downloadFolder);
                    var target = Path.Combine(_downloadFolder, Path.GetFileName(path));
                    if (File.Exists(target)) return new ToolResult(false, $"{target} already exists; nothing was replaced.");
                    var (exit, output) = await _rclone(new[] { "copyto", Remote + path, target }, cancellationToken);
                    return exit != 0 ? Failed(output) : new ToolResult(true, $"Downloaded to {target}");
                }
                case "upload":
                {
                    var local = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Arg("path")));
                    if (!File.Exists(local)) return new ToolResult(false, $"File not found: {local}");
                    var folder = DrivePath(Arg("folder"));
                    var destination = Remote + (folder.Length == 0 ? "" : folder + "/") + Path.GetFileName(local);
                    var (exit, output) = await _rclone(new[] { "copyto", local, destination, "--ignore-existing" }, cancellationToken);
                    return exit != 0 ? Failed(output) : new ToolResult(true,
                        $"Uploaded {Path.GetFileName(local)} to Drive{(folder.Length == 0 ? "" : " / " + folder)} (an existing file with the same name is left untouched).");
                }
                case "space":
                {
                    var (exit, output) = await _rclone(new[] { "about", Remote }, cancellationToken);
                    return exit != 0 ? Failed(output) : new ToolResult(true, output.Trim());
                }
                default:
                    return new ToolResult(false, "action must be list, search, download, upload or space.");
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception or IOException or JsonException or ArgumentException)
        {
            return new ToolResult(false, "Google Drive failed: " + ex.Message);
        }
    }

    private static ToolResult Failed(string output) =>
        output.Contains("didn't find section in config file", StringComparison.OrdinalIgnoreCase) || output.Contains("not found", StringComparison.OrdinalIgnoreCase) && output.Contains("gdrive")
            ? new ToolResult(false, "Google Drive is not connected yet. Run scripts\\Connect-GoogleDrive.ps1 and sign in to Google in the browser.")
            : new ToolResult(false, "Google Drive error: " + Last(output));

    private static string Last(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault() ?? "unknown error";

    /// <summary>Drive paths are relative; ".." and leading slashes are removed so a path cannot escape the remote.</summary>
    public static string DrivePath(string path) =>
        string.Join('/', (path ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p is not ("." or "..")));

    private static string EscapeGlob(string text) =>
        string.Concat(text.Select(c => c is '*' or '?' or '[' or ']' or '{' or '}' or '\\' ? "\\" + c : c.ToString()));

    private static string Format(string json, string folder, int max, bool includePath = false)
    {
        using var doc = JsonDocument.Parse(json.Trim().Length == 0 ? "[]" : json);
        var items = doc.RootElement.EnumerateArray().ToList();
        if (items.Count == 0) return includePath ? "No matching files in Drive." : $"Drive{(folder.Length == 0 ? "" : " / " + folder)} is empty.";
        var lines = items
            .OrderByDescending(i => i.GetProperty("IsDir").GetBoolean())
            .ThenBy(i => i.GetProperty("Name").GetString(), StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(i => i.GetProperty("IsDir").GetBoolean()
                ? $"  [folder] {i.GetProperty("Name").GetString()}"
                : $"  {(includePath ? i.GetProperty("Path").GetString() : i.GetProperty("Name").GetString())} ({Size(i.GetProperty("Size").GetInt64())}, {i.GetProperty("ModTime").GetDateTimeOffset().LocalDateTime:yyyy-MM-dd})");
        var text = string.Join("\n", lines);
        return items.Count > max ? text + $"\n  ... {items.Count - max} more." : text;
    }

    private static string Size(long bytes) => bytes switch
    {
        < 0 => "Google Doc",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB"
    };

    private static async Task<(int, string)> RunRcloneAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var exe = Environment.GetEnvironmentVariable("JARVIS_RCLONE") ?? FindRclone()
                  ?? throw new FileNotFoundException("rclone is not installed (expected JARVIS-tools\\rclone\\rclone.exe).");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new IOException("Could not start rclone.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new IOException("Google Drive took too long.");
        }
        var output = await stdout;
        return (process.ExitCode, process.ExitCode == 0 ? output : output + "\n" + await stderr);
    }

    private static string? FindRclone()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "JARVIS.Council.sln"))) continue;
            var candidate = dir.Parent is null ? null : Path.Combine(dir.Parent.FullName, "JARVIS-tools", "rclone", "rclone.exe");
            return candidate is not null && File.Exists(candidate) ? candidate : null;
        }
        return null;
    }
}
