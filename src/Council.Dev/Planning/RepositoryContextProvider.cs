namespace Council.Dev.Planning;

public sealed class RepositoryContextProvider
{
    private static readonly string[] IgnoredDirectories =
    [
        "bin",
        "obj",
        ".git",
        ".vs",
        "node_modules"
    ];

    private readonly string _workingDirectory;

    public RepositoryContextProvider(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException(
                "Working directory is required.",
                nameof(workingDirectory));
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Working directory does not exist: {workingDirectory}");
        }

        _workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public Task<RepositoryContext> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var files = Directory
            .EnumerateFiles(
                _workingDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Select(ToRelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projects = files
            .Where(path =>
                path.EndsWith(
                    ".csproj",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(
            new RepositoryContext(
                _workingDirectory,
                files,
                projects));
    }

    private bool IsIgnored(string path)
    {
        var relative = ToRelativePath(path);

        var parts = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return parts.Any(part =>
            IgnoredDirectories.Contains(
                part,
                StringComparer.OrdinalIgnoreCase));
    }

    private string ToRelativePath(string path)
    {
        return Path.GetRelativePath(
                _workingDirectory,
                path)
            .Replace(
                Path.DirectorySeparatorChar,
                '/');
    }
}
