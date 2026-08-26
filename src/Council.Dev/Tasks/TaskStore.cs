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

        await using var stream = File.OpenRead(_filePath);

        return await JsonSerializer.DeserializeAsync<List<DevelopmentTask>>(
                   stream,
                   _jsonOptions,
                   cancellationToken)
               ?? [];
    }

    public async Task SaveAsync(
        List<DevelopmentTask> tasks,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);

        await JsonSerializer.SerializeAsync(
            stream,
            tasks,
            _jsonOptions,
            cancellationToken);
    }
}
