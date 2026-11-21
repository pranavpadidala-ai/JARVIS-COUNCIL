using Council.Execution.Tools;
using Microsoft.VisualBasic.FileIO;

namespace Council.App.Files;

/// <summary>
/// Manages the owner's own files outside the workspace: list, find, info, copy, move, rename and
/// delete. Delete always goes to the Recycle Bin so it can be undone. Windows, program folders,
/// the AI model folder and hidden system folders are off limits, and nothing is ever overwritten.
/// </summary>
public sealed class FileManagerTool : ITool
{
    private const int MaxListed = 200;
    private const int MaxFound = 100;
    private readonly IReadOnlyList<string> _blockedRoots;
    private readonly Action<string> _recycle;

    public FileManagerTool(IEnumerable<string>? blockedRoots = null, Action<string>? recycle = null)
    {
        _blockedRoots = (blockedRoots ?? DefaultBlockedRoots())
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Normalize(Path.GetFullPath(root)))
            .ToList();
        _recycle = recycle ?? SendToRecycleBin;
    }

    public string Name => "file_manager";

    public string Description =>
        "Manage the user's files and folders anywhere on this laptop (Desktop, Documents, Downloads, other drives). " +
        "Arguments: action (list|find|info|copy|move|rename|delete). " +
        "list: path. find: path (folder to search, e.g. the user's Documents) and pattern (name or wildcard like *.pdf). " +
        "info: path. copy/move: path and to (destination folder or full new path). rename: path and new_name. " +
        "delete: path (goes to the Recycle Bin, so it can be restored). Never overwrites an existing file. " +
        "Windows, Program Files and the AI model folder cannot be changed. Use known folders like " +
        "'Desktop', 'Documents', 'Downloads', 'Pictures', 'Music', 'Videos' as shortcuts.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        var action = Get(args, "action").ToLowerInvariant();
        try
        {
            return Task.FromResult(action switch
            {
                "list" => List(Resolve(Get(args, "path"))),
                "find" => Find(Resolve(Get(args, "path")), Get(args, "pattern"), cancellationToken),
                "info" => Info(Resolve(Get(args, "path"))),
                "copy" => Transfer(args, move: false),
                "move" => Transfer(args, move: true),
                "rename" => Rename(args),
                "delete" => Delete(Resolve(Get(args, "path"))),
                _ => new ToolResult(false, "action must be list, find, info, copy, move, rename or delete.")
            });
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return Task.FromResult(new ToolResult(false, ex.Message));
        }
    }

    private static ToolResult List(string path)
    {
        if (!Directory.Exists(path)) return new ToolResult(false, $"Folder not found: {path}");
        var dir = new DirectoryInfo(path);
        var entries = dir.EnumerateFileSystemInfos()
            .Where(e => !e.Attributes.HasFlag(FileAttributes.Hidden) && !e.Attributes.HasFlag(FileAttributes.System))
            .OrderBy(e => e is FileInfo).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListed + 1)
            .ToList();
        var lines = entries.Take(MaxListed).Select(e => e is FileInfo f
            ? $"  {f.Name}  ({FormatSize(f.Length)}, {f.LastWriteTime:yyyy-MM-dd HH:mm})"
            : $"  [folder] {e.Name}");
        var text = $"{path}\n" + (entries.Count == 0 ? "  (empty)" : string.Join("\n", lines));
        if (entries.Count > MaxListed) text += $"\n  ... more than {MaxListed} items; use find to narrow it down.";
        return new ToolResult(true, text);
    }

    private static ToolResult Find(string root, string pattern, CancellationToken token)
    {
        if (!Directory.Exists(root)) return new ToolResult(false, $"Folder not found: {root}");
        if (string.IsNullOrWhiteSpace(pattern)) return new ToolResult(false, "pattern is required, e.g. *.pdf or report.");
        var glob = pattern.Contains('*') || pattern.Contains('?') ? pattern : $"*{pattern}*";
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };
        var found = new List<string>();
        foreach (var item in Directory.EnumerateFileSystemEntries(root, glob, options))
        {
            token.ThrowIfCancellationRequested();
            found.Add(item);
            if (found.Count > MaxFound) break;
        }
        if (found.Count == 0) return new ToolResult(true, $"Nothing matching '{pattern}' under {root}.");
        var text = string.Join("\n", found.Take(MaxFound));
        if (found.Count > MaxFound) text += $"\n... more than {MaxFound} matches; use a more specific pattern.";
        return new ToolResult(true, text);
    }

    private static ToolResult Info(string path)
    {
        if (File.Exists(path))
        {
            var f = new FileInfo(path);
            return new ToolResult(true,
                $"{f.FullName}\nSize: {FormatSize(f.Length)}\nModified: {f.LastWriteTime:yyyy-MM-dd HH:mm}\nCreated: {f.CreationTime:yyyy-MM-dd HH:mm}");
        }
        if (Directory.Exists(path))
        {
            var d = new DirectoryInfo(path);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            long size = 0; var files = 0;
            foreach (var f in d.EnumerateFiles("*", options)) { size += f.Length; if (++files >= 20000) break; }
            return new ToolResult(true,
                $"{d.FullName} (folder)\nFiles: {files}{(files >= 20000 ? "+" : "")}\nTotal size: {FormatSize(size)}\nModified: {d.LastWriteTime:yyyy-MM-dd HH:mm}");
        }
        return new ToolResult(false, $"Not found: {path}");
    }

    private ToolResult Transfer(Dictionary<string, string> args, bool move)
    {
        var source = Resolve(Get(args, "path"));
        var to = Get(args, "to");
        if (string.IsNullOrWhiteSpace(to)) return new ToolResult(false, "to (destination) is required.");
        var destination = Resolve(to);
        var isFile = File.Exists(source);
        if (!isFile && !Directory.Exists(source)) return new ToolResult(false, $"Not found: {source}");
        if (Directory.Exists(destination)) destination = Path.Combine(destination, Path.GetFileName(source));
        EnsureAllowed(destination);
        if (move) EnsureAllowed(source);
        if (File.Exists(destination) || Directory.Exists(destination))
            return new ToolResult(false, $"Something already exists at {destination}; nothing was overwritten.");
        if (!isFile && IsInside(destination, source))
            return new ToolResult(false, "A folder cannot be copied or moved into itself.");
        var parent = Path.GetDirectoryName(destination);
        if (parent is null || !Directory.Exists(parent)) return new ToolResult(false, $"Destination folder not found: {parent}");

        if (isFile)
        {
            if (move) File.Move(source, destination); else File.Copy(source, destination);
        }
        else if (move)
        {
            if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
                Directory.Move(source, destination);
            else
                FileSystem.MoveDirectory(source, destination);
        }
        else
        {
            FileSystem.CopyDirectory(source, destination);
        }
        return new ToolResult(true, $"{(move ? "Moved" : "Copied")} {source} -> {destination}");
    }

    private ToolResult Rename(Dictionary<string, string> args)
    {
        var source = Resolve(Get(args, "path"));
        var newName = Get(args, "new_name");
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new ToolResult(false, "new_name must be a plain file or folder name.");
        EnsureAllowed(source);
        var destination = Path.Combine(Path.GetDirectoryName(source) ?? "", newName);
        if (File.Exists(destination) || Directory.Exists(destination))
            return new ToolResult(false, $"{newName} already exists there; nothing was renamed.");
        if (File.Exists(source)) File.Move(source, destination);
        else if (Directory.Exists(source)) Directory.Move(source, destination);
        else return new ToolResult(false, $"Not found: {source}");
        return new ToolResult(true, $"Renamed {source} -> {destination}");
    }

    private ToolResult Delete(string path)
    {
        EnsureAllowed(path);
        if (!File.Exists(path) && !Directory.Exists(path)) return new ToolResult(false, $"Not found: {path}");
        if (Path.GetPathRoot(path) is { } root && string.Equals(Normalize(root), Normalize(path), StringComparison.OrdinalIgnoreCase))
            return new ToolResult(false, "A whole drive cannot be deleted.");
        _recycle(path);
        return new ToolResult(true, $"Moved to the Recycle Bin (restore it from there if needed): {path}");
    }

    private static void SendToRecycleBin(string path)
    {
        if (File.Exists(path))
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.");
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        var known = KnownFolder(path);
        if (known is not null) return known;
        var first = path.Split('\\', '/')[0];
        var knownPrefix = KnownFolder(first);
        if (knownPrefix is not null && !Path.IsPathRooted(path))
            path = Path.Combine(knownPrefix, path[first.Length..].TrimStart('\\', '/'));
        else if (!Path.IsPathRooted(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path);
        return Normalize(Path.GetFullPath(path));
    }

    private static string? KnownFolder(string name) => name.ToLowerInvariant() switch
    {
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "documents" or "my documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "home" or "~" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        _ => null
    };

    private void EnsureAllowed(string path)
    {
        var full = Normalize(path);
        foreach (var root in _blockedRoots)
            if (IsInside(full, root))
                throw new UnauthorizedAccessException($"JARVIS is not allowed to change files in {root}.");
        var name = Path.GetFileName(full);
        if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) || full.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("JARVIS does not change git internals.");
    }

    private static bool IsInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> DefaultBlockedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData");
        yield return @"E:\JARVIS\Models";
        var extra = Environment.GetEnvironmentVariable("JARVIS_FILES_BLOCKED");
        if (!string.IsNullOrWhiteSpace(extra))
            foreach (var p in extra.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) yield return p;
    }

    private static string Normalize(string path) =>
        path.Length > 3 ? path.TrimEnd('\\', '/') : path;

    private static string Get(Dictionary<string, string> args, string key) =>
        args.TryGetValue(key, out var value) ? value.Trim() : "";

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B"
    };
}
