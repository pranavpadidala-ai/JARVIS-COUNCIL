using System.Text.Json;

namespace Council.Dev.Tasks;

public sealed class TaskStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public TaskStore(string? filePath = null)
    {
        _filePath = filePath ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "JARVIS",
                "development-tasks.json");
    }

    public async Task<List<DevelopmentTask>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var info = new FileInfo(_filePath);
        if (info.Length > 5 * 1024 * 1024)
            throw new InvalidDataException("Development task store exceeds the 5 MB safety limit.");

        await using var stream = File.OpenRead(_filePath);
        var tasks = await JsonSerializer.DeserializeAsync<List<DevelopmentTask>>(
                        stream,
                        _jsonOptions,
                        cancellationToken)
                    ?? [];

        if (tasks.Count > 500)
            throw new InvalidDataException("Development task store exceeds the 500-task safety limit.");

        return tasks;
    }

    public async Task SaveAsync(
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

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
