using System.Text.Json;

namespace Council.Dev.Tasks;

public sealed class TaskStore
{
    private readonly string _filePath;
    private readonly string _lockPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public TaskStore(string? filePath = null)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _filePath = Path.GetFullPath(filePath ??
            Path.Combine(localData, "JARVIS", "development-tasks.json"));
        _lockPath = _filePath + ".lock";
    }

    /// <summary>
    /// Acquires a cross-process exclusive lock for read-modify-write task queue operations.
    /// The caller must dispose the returned stream after using the unlocked methods.
    /// </summary>
    public async Task<FileStream> AcquireExclusiveLockAsync(
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    useAsync: true);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
    }

    public async Task<List<DevelopmentTask>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await using var fileLock = await AcquireExclusiveLockAsync(cancellationToken);
        return await LoadUnlockedAsync(cancellationToken);
    }

    internal async Task<List<DevelopmentTask>> LoadUnlockedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return [];

        var info = new FileInfo(_filePath);
        if (info.Length > 5 * 1024 * 1024)
            throw new InvalidDataException("Development task store exceeds the 5 MB safety limit.");

        await using var stream = File.OpenRead(_filePath);
        var tasks = await JsonSerializer.DeserializeAsync<List<DevelopmentTask>>(
            stream,
            _jsonOptions,
            cancellationToken) ?? [];

        if (tasks.Count > 500)
            throw new InvalidDataException("Development task store exceeds the 500-task safety limit.");

        return tasks;
    }

    public async Task SaveAsync(
        List<DevelopmentTask> tasks,
        CancellationToken cancellationToken = default)
    {
        await using var fileLock = await AcquireExclusiveLockAsync(cancellationToken);
        await SaveUnlockedAsync(tasks, cancellationToken);
    }

    internal async Task SaveUnlockedAsync(
        List<DevelopmentTask> tasks,
        CancellationToken cancellationToken = default)
    {
        if (tasks.Count > 500)
            throw new InvalidOperationException("JARVIS can retain at most 500 development tasks.");

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    tasks,
                    _jsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
