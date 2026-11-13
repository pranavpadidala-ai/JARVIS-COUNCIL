using Council.Dev.Actions;

namespace Council.Dev.Workspace;

public sealed class SafeWorkspaceExecutor : IWorkspaceExecutor
{
    private const long MaxReadableFileBytes = 2 * 1024 * 1024;
    private const int MaxSearchMatches = 500;

    private static readonly string[] ProtectedSegments =
    [
        ".git",
        "bin",
        "obj"
    ];

    private static readonly string[] ProtectedFileNames =
    [
        ".env",
        ".env.local",
        "appsettings.json",
        "appsettings.Development.json"
    ];

    private readonly string _workspaceRoot;

    public SafeWorkspaceExecutor(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException(
                "Workspace root is required.",
                nameof(workspaceRoot));
        }

        _workspaceRoot = Path.GetFullPath(workspaceRoot);

        if (!Directory.Exists(_workspaceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Workspace directory does not exist: {_workspaceRoot}");
        }

        if ((File.GetAttributes(_workspaceRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Workspace root cannot be a symbolic link or reparse point.");
        }
    }

    public async Task<WorkspaceActionResult> ExecuteAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();

        return action.Type switch
        {
            DevelopmentActionType.ReadFile =>
                await ReadFileAsync(action, cancellationToken),

            DevelopmentActionType.SearchCode =>
                await SearchCodeAsync(action, cancellationToken),

            DevelopmentActionType.ListFiles =>
                await ListFilesAsync(action, cancellationToken),

            DevelopmentActionType.CreateFile =>
                await CreateFileAsync(action, cancellationToken),

            DevelopmentActionType.ModifyFile =>
                await ModifyFileAsync(action, cancellationToken),

            DevelopmentActionType.DeleteFile =>
                await DeleteFileAsync(action, cancellationToken),

            DevelopmentActionType.RunBuild =>
                WorkspaceActionResult.Failed(
                    "Build execution is not handled by the workspace executor."),

            DevelopmentActionType.RunTests =>
                WorkspaceActionResult.Failed(
                    "Test execution is not handled by the workspace executor."),

            DevelopmentActionType.GitDiff =>
                WorkspaceActionResult.Failed(
                    "Git operations are not handled by the workspace executor."),

            _ =>
                WorkspaceActionResult.Failed(
                    $"Unsupported development action: {action.Type}.")
        };
    }

    private async Task<WorkspaceActionResult> ReadFileAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken)
    {
        var pathResult = ResolveSafePath(action.Target);

        if (!pathResult.Success)
        {
            return pathResult.Result!;
        }

        if (!File.Exists(pathResult.Path!))
        {
            return WorkspaceActionResult.Failed(
                $"File does not exist: {action.Target}");
        }

        var fileInfo = new FileInfo(pathResult.Path!);

        if (fileInfo.Length > MaxReadableFileBytes)
        {
            return WorkspaceActionResult.Failed(
                $"File exceeds the 2 MB read limit: {action.Target}");
        }

        var content = await File.ReadAllTextAsync(
            pathResult.Path!,
            cancellationToken);

        return WorkspaceActionResult.Completed(
            $"Read file: {action.Target}",
            content);
    }

    private async Task<WorkspaceActionResult> SearchCodeAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Target))
        {
            return WorkspaceActionResult.Failed(
                "Search pattern is required.");
        }

        var results = new List<string>();

        foreach (var file in EnumerateWorkspaceFiles(_workspaceRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsProtectedPath(file))
            {
                continue;
            }

            if (!IsCodeFile(file))
            {
                continue;
            }

            var fileInfo = new FileInfo(file);

            if (fileInfo.Length > MaxReadableFileBytes)
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(
                file,
                cancellationToken);

            if (content.Contains(
                action.Target,
                StringComparison.OrdinalIgnoreCase))
            {
                results.Add(
                    Path.GetRelativePath(
                        _workspaceRoot,
                        file));

                if (results.Count >= MaxSearchMatches)
                {
                    break;
                }
            }
        }

        return WorkspaceActionResult.Completed(
            results.Count >= MaxSearchMatches
                ? $"Search stopped at the {MaxSearchMatches} match limit."
                : $"Search completed. Matches: {results.Count}.",
            string.Join(
                Environment.NewLine,
                results));
    }

    private async Task<WorkspaceActionResult> CreateFileAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken)
    {
        if (action.Content is null)
        {
            return WorkspaceActionResult.Failed(
                "File content is required.");
        }

        var pathResult = ResolveSafePath(action.Target);

        if (!pathResult.Success)
        {
            return pathResult.Result!;
        }

        if (File.Exists(pathResult.Path!))
        {
            return WorkspaceActionResult.Failed(
                $"File already exists: {action.Target}");
        }

        var directory = Path.GetDirectoryName(pathResult.Path!);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var writeResult = await WriteFileAtomicallyAsync(
            pathResult.Path!,
            action.Content,
            overwrite: false,
            cancellationToken);

        return writeResult
            ? WorkspaceActionResult.Completed($"Created file: {action.Target}")
            : WorkspaceActionResult.Failed($"File already exists: {action.Target}");
    }

    private async Task<WorkspaceActionResult> ModifyFileAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken)
    {
        if (action.Content is null)
        {
            return WorkspaceActionResult.Failed(
                "File content is required.");
        }

        var pathResult = ResolveSafePath(action.Target);

        if (!pathResult.Success)
        {
            return pathResult.Result!;
        }

        if (!File.Exists(pathResult.Path!))
        {
            return WorkspaceActionResult.Failed(
                $"File does not exist: {action.Target}");
        }

        await WriteFileAtomicallyAsync(
            pathResult.Path!,
            action.Content,
            overwrite: true,
            cancellationToken);

        return WorkspaceActionResult.Completed(
            $"Modified file: {action.Target}");
    }

    private static async Task<bool> WriteFileAtomicallyAsync(
        string destinationPath,
        string content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException(
                "Destination directory could not be resolved.");

        var temporaryPath = Path.Combine(
            directory,
            $".jarvis-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // The temporary file is created beside the destination so the
            // final rename stays on the same volume.
            File.Move(
                temporaryPath,
                destinationPath,
                overwrite);

            return true;
        }
        catch (IOException) when (!overwrite && File.Exists(destinationPath))
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Preserve the primary write result; a stale temp file is
                // safer than masking the original exception.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }

    private Task<WorkspaceActionResult> DeleteFileAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pathResult = ResolveSafePath(action.Target);

        if (!pathResult.Success)
        {
            return Task.FromResult(pathResult.Result!);
        }

        if (!File.Exists(pathResult.Path!))
        {
            return Task.FromResult(
                WorkspaceActionResult.Failed(
                    $"File does not exist: {action.Target}"));
        }

        if (IsProtectedPath(pathResult.Path!))
        {
            return Task.FromResult(
                WorkspaceActionResult.Failed(
                    $"Protected path cannot be deleted: {action.Target}"));
        }

        File.Delete(pathResult.Path!);

        return Task.FromResult(
            WorkspaceActionResult.Completed(
                $"Deleted file: {action.Target}"));
    }

    private (bool Success, string? Path, WorkspaceActionResult? Result)
        ResolveSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return (
                false,
                null,
                WorkspaceActionResult.Failed(
                    "Target path is required."));
        }

        var fullPath = Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                relativePath));

        if (!IsInsideWorkspace(fullPath))
        {
            return (
                false,
                null,
                WorkspaceActionResult.Failed(
                    "Target path is outside the workspace."));
        }

        if (IsProtectedPath(fullPath))
        {
            return (
                false,
                null,
                WorkspaceActionResult.Failed(
                    "Target path is protected."));
        }

        if (HasReparsePointInPath(fullPath))
        {
            return (
                false,
                null,
                WorkspaceActionResult.Failed(
                    "Symbolic links and reparse-point paths are not allowed."));
        }

        return (true, fullPath, null);
    }

    private Task<WorkspaceActionResult> ListFilesAsync(
        DevelopmentAction action,
        CancellationToken cancellationToken)
    {
        var pathResult = ResolveSafePath(
            string.IsNullOrWhiteSpace(action.Target) ? "." : action.Target);

        if (!pathResult.Success)
        {
            return Task.FromResult(pathResult.Result!);
        }

        if (!Directory.Exists(pathResult.Path!))
        {
            return Task.FromResult(WorkspaceActionResult.Failed(
                $"Directory does not exist: {action.Target}"));
        }

        var files = EnumerateWorkspaceFiles(
                pathResult.Path!,
                cancellationToken,
                codeFilesOnly: false)
            .Select(file => Path.GetRelativePath(_workspaceRoot, file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .Take(5001)
            .ToArray();

        var truncated = files.Length > 5000;
        var visibleFiles = files.Take(5000);

        return Task.FromResult(WorkspaceActionResult.Completed(
            truncated
                ? "File listing completed. Output limited to 5000 files."
                : $"File listing completed. Files: {files.Length}.",
            string.Join(Environment.NewLine, visibleFiles)));
    }

    private IEnumerable<string> EnumerateWorkspaceFiles(
        string searchRoot,
        CancellationToken cancellationToken,
        bool codeFilesOnly = true)
    {
        // Traverse one directory at a time so protected folders are never
        // recursively entered. Reparse points are skipped to avoid symlink
        // loops and escapes from the workspace.
        var pending = new Stack<string>();
        pending.Push(searchRoot);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!IsProtectedPath(entry))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                if (!IsProtectedPath(entry) && (!codeFilesOnly || IsCodeFile(entry)))
                {
                    yield return entry;
                }
            }
        }
    }

    private bool HasReparsePointInPath(string fullPath)
    {
        var current = fullPath;

        while (!string.IsNullOrWhiteSpace(current)
               && !string.Equals(
                   current,
                   Path.GetPathRoot(current),
                   StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
            }

            current = Path.GetDirectoryName(current)!;
        }

        return false;
    }

    private bool IsInsideWorkspace(string fullPath)
    {
        var rootWithSeparator =
            _workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(
                   rootWithSeparator,
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   fullPath,
                   _workspaceRoot,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCodeFile(string path)
    {
        var extension =
            Path.GetExtension(path);

        return extension.Equals(
                   ".cs",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".csproj",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".sln",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".json",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".xml",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".md",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedPath(string path)
    {
        var normalized =
            Path.GetFullPath(path);

        var currentName = Path.GetFileName(
            normalized.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));

        if (ProtectedSegments.Any(segment =>
            string.Equals(
                segment,
                currentName,
                StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var segment in ProtectedSegments)
        {
            var directoryName =
                Path.GetDirectoryName(normalized);

            while (!string.IsNullOrWhiteSpace(directoryName))
            {
                if (string.Equals(
                    Path.GetFileName(directoryName),
                    segment,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                directoryName =
                    Path.GetDirectoryName(directoryName);
            }
        }

        var fileName =
            Path.GetFileName(normalized);

        return ProtectedFileNames.Any(name =>
            string.Equals(
                name,
                fileName,
                StringComparison.OrdinalIgnoreCase));
    }
}



