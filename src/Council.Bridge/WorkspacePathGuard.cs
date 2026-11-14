namespace Council.Bridge;

/// <summary>
/// Restricts bridge file access to the configured repository root and rejects
/// path traversal and existing reparse-point (symbolic-link/junction) segments.
/// </summary>
public sealed class WorkspacePathGuard
{
    private static readonly HashSet<string> ReadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".md", ".ps1", ".xaml", ".yml", ".yaml",
        ".xml", ".props", ".targets", ".txt"
    };

    private static readonly HashSet<string> HiddenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", ".idea", ".vscode"
    };

    public string Root { get; }

    public WorkspacePathGuard(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("A workspace root is required.", nameof(root));

        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root))
            throw new DirectoryNotFoundException("Workspace root does not exist: " + Root);
        if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("A symbolic link or junction cannot be used as the workspace root.");
    }

    public string Resolve(string relativePath)
    {
        relativePath ??= string.Empty;
        if (Path.IsPathRooted(relativePath))
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");

        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains(':') || HiddenNames.Contains(segment) || IsSensitiveName(segment)))
            throw new UnauthorizedAccessException("Path traversal, sensitive directories and drive-qualified paths are not allowed.");

        var fullPath = Path.GetFullPath(Path.Combine(Root, normalized));
        var relative = Path.GetRelativePath(Root, fullPath);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException("The requested path is outside the workspace.");

        var current = Root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Symbolic links and junctions are not accessible through the bridge.");
            }
        }

        return fullPath;
    }

    public string ReadTextFile(string relativePath, int maxBytes = 256 * 1024)
    {
        var fullPath = Resolve(relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Workspace file was not found.", relativePath);

        var fileName = Path.GetFileName(fullPath);
        if (IsSensitiveName(fileName) || !ReadableExtensions.Contains(Path.GetExtension(fullPath)))
            throw new UnauthorizedAccessException("This file name or type is not readable through the development bridge.");

        var info = new FileInfo(fullPath);
        if (info.Length > maxBytes)
            throw new InvalidOperationException($"File exceeds the {maxBytes} byte read limit.");

        return File.ReadAllText(fullPath);
    }

    private static bool IsSensitiveName(string name)
    {
        var value = name.ToLowerInvariant();
        return value.StartsWith(".env", StringComparison.Ordinal) ||
               value.Contains("secret", StringComparison.Ordinal) ||
               value.Contains("credential", StringComparison.Ordinal) ||
               value.Contains("password", StringComparison.Ordinal) ||
               value.Contains("token", StringComparison.Ordinal) ||
               value.Contains("api-key", StringComparison.Ordinal) ||
               value.Contains("apikey", StringComparison.Ordinal);
    }

    public IReadOnlyList<object> ListEntries(string relativePath = "")
    {
        var directory = Resolve(relativePath);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("Workspace directory was not found.");

        return Directory.EnumerateFileSystemEntries(directory)
            .Where(path => !HiddenNames.Contains(Path.GetFileName(path)) && !IsSensitiveName(Path.GetFileName(path)))
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .Select(path => (object)new
            {
                name = Path.GetFileName(path),
                kind = Directory.Exists(path) ? "directory" : "file",
                relativePath = Path.GetRelativePath(Root, path).Replace('\\', '/')
            })
            .ToArray();
    }
}
