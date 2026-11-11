using System.Text;
using Council.Execution.Tools;

namespace Council.App;

public sealed class WorkspaceTool : ITool
{
    private const int MaxReadBytes = 1_000_000;
    private const int MaxWriteBytes = 250_000;
    private const int MaxSearchResults = 50;
    private static readonly HashSet<string> BlockedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs"
    };
    private static readonly HashSet<string> BlockedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".env.local", ".env.production", "appsettings.json",
        "appsettings.development.json", "secrets.json"
    };
    private static readonly HashSet<string> SearchExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".json", ".xml", ".md", ".txt", ".yml", ".yaml"
    };

    private readonly string _root;
    public string Name { get; }
    public string Description { get; }

    public WorkspaceTool(string name, string description, string workspaceRoot)
    {
        Name = name;
        Description = description;
        _root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException("Workspace root does not exist.");
        if (IsReparsePoint(_root))
            throw new InvalidOperationException("Workspace root cannot be a symbolic link or reparse point.");
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Name switch
            {
                "list_files" => ListFiles(Required(arguments, "path", allowEmpty: true)),
                "read_file" => await ReadFileAsync(Required(arguments, "path"), cancellationToken),
                "search_files" => await SearchFilesAsync(Required(arguments, "query"), cancellationToken),
                "create_file" => await CreateFileAsync(Required(arguments, "path"), Required(arguments, "content", allowEmpty: true), cancellationToken),
                "create_folder" => CreateFolder(Required(arguments, "path")),
                _ => new ToolResult(false, "Unknown workspace tool.")
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new ToolResult(false, ex.Message);
        }
    }

    private ToolResult ListFiles(string relative)
    {
        var directory = Resolve(relative, allowRoot: true);
        if (!Directory.Exists(directory)) return new ToolResult(false, "Folder does not exist.");
        var entries = Directory.EnumerateFileSystemEntries(directory)
            .Where(p => !IsBlockedEntry(p) && !IsReparsePoint(p))
            .Take(200)
            .Select(p => (Directory.Exists(p) ? "[DIR] " : "[FILE] ") + Path.GetRelativePath(_root, p))
            .ToArray();
        return new ToolResult(true, entries.Length == 0 ? "No visible files or folders." : string.Join(Environment.NewLine, entries));
    }

    private async Task<ToolResult> ReadFileAsync(string relative, CancellationToken token)
    {
        var path = Resolve(relative);
        if (!File.Exists(path)) return new ToolResult(false, "File does not exist.");
        var info = new FileInfo(path);
        if (info.Length > MaxReadBytes) return new ToolResult(false, "File exceeds the 1 MB read limit.");
        var content = await File.ReadAllTextAsync(path, token);
        return new ToolResult(true, content);
    }

    private async Task<ToolResult> SearchFilesAsync(string query, CancellationToken token)
    {
        if (query.Length > 300) return new ToolResult(false, "Search query is too long (maximum 300 characters).");
        var matches = new List<string>();
        var pending = new Stack<string>();
        pending.Push(_root);
        while (pending.Count > 0 && matches.Count < MaxSearchResults)
        {
            token.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                token.ThrowIfCancellationRequested();
                if (IsBlockedEntry(entry) || IsReparsePoint(entry)) continue;
                if (Directory.Exists(entry)) { pending.Push(entry); continue; }
                if (!SearchExtensions.Contains(Path.GetExtension(entry))) continue;
                var info = new FileInfo(entry);
                if (info.Length > MaxReadBytes) continue;
                var content = await File.ReadAllTextAsync(entry, token);
                if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
                    matches.Add(Path.GetRelativePath(_root, entry));
                if (matches.Count >= MaxSearchResults) break;
            }
        }
        return new ToolResult(true, matches.Count == 0 ? "No matches found." : string.Join(Environment.NewLine, matches));
    }

    private async Task<ToolResult> CreateFileAsync(string relative, string content, CancellationToken token)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaxWriteBytes)
            return new ToolResult(false, "New file exceeds the 250 KB write limit.");
        var path = Resolve(relative);
        if (File.Exists(path) || Directory.Exists(path)) return new ToolResult(false, "A file or folder already exists at that path. Existing files are never overwritten.");
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) return new ToolResult(false, "Parent folder does not exist. Create it with create_folder first.");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), token);
        return new ToolResult(true, "Created " + Path.GetRelativePath(_root, path));
    }

    private ToolResult CreateFolder(string relative)
    {
        var path = Resolve(relative);
        if (File.Exists(path)) return new ToolResult(false, "A file already exists at that path.");
        if (Directory.Exists(path)) return new ToolResult(true, "Folder already exists.");
        Directory.CreateDirectory(path);
        // Re-check after creation so no pre-existing linked parent was followed.
        _ = Resolve(relative, allowRoot: true);
        return new ToolResult(true, "Created folder " + Path.GetRelativePath(_root, path));
    }

    private string Resolve(string relative, bool allowRoot = false)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            if (allowRoot) return _root;
            throw new ArgumentException("A relative path is required.");
        }
        if (Path.IsPathRooted(relative)) throw new ArgumentException("Absolute paths are not allowed.");
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, _root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path is outside the JARVIS workspace.");
        if (!allowRoot && string.Equals(full, _root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The workspace root itself is not a file path.");
        var relativeFull = Path.GetRelativePath(_root, full);
        var current = _root;
        foreach (var segment in relativeFull.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment is "." or "") continue;
            if (BlockedDirectories.Contains(segment) || BlockedFiles.Contains(segment))
                throw new ArgumentException("Access to protected files or folders is blocked.");
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                throw new ArgumentException("Symbolic links and reparse points are not accessible.");
        }
        return full;
    }

    private static string Required(Dictionary<string, string> args, string key, bool allowEmpty = false)
    {
        if (!args.TryGetValue(key, out var value) || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("Missing required argument: " + key);
        return value;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool IsBlockedEntry(string path) =>
        BlockedDirectories.Contains(Path.GetFileName(path)) ||
        BlockedFiles.Contains(Path.GetFileName(path));
}
