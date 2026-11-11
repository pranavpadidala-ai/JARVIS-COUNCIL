using System.Text.Json;
using Council.Core.Models;

namespace Council.Memory;

/// <summary>
/// Optional, local-only JSONL conversation history. It stores user/assistant
/// messages only; system prompts and credentials are not intentionally persisted.
/// Enable it explicitly with JARVIS_MEMORY_ENABLED=true.
/// </summary>
public sealed class ConversationMemoryStore
{
    private const long CompactThresholdBytes = 2 * 1024 * 1024;
    private const int RetainedMessagesAfterCompaction = 1000;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConversationMemoryStore(string? filePath = null)
    {
        var configuredPath = filePath ??
            Environment.GetEnvironmentVariable("JARVIS_MEMORY_PATH");

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configuredPath = Path.Combine(root, "JARVIS-Council", "Memory", "conversation.jsonl");
        }

        _filePath = Path.GetFullPath(configuredPath);
    }

    public string FilePath => _filePath;

    public async Task<IReadOnlyList<AIMessage>> LoadRecentAsync(
        int maxMessages = 25,
        CancellationToken cancellationToken = default)
    {
        if (maxMessages is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<AIMessage>();

            var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
            var messages = new List<AIMessage>();
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var stored = JsonSerializer.Deserialize<StoredMessage>(line);
                    if (stored is not null && IsPersistableRole(stored.Role))
                        messages.Add(new AIMessage(stored.Role, stored.Content ?? string.Empty));
                }
                catch (JsonException)
                {
                    // Ignore a damaged or partially written record and continue.
                }
            }

            return messages.TakeLast(maxMessages).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(
        AIMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsPersistableRole(message.Role) || string.IsNullOrWhiteSpace(message.Content))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var record = new StoredMessage(
                DateTimeOffset.UtcNow,
                message.Role.Trim().ToLowerInvariant(),
                message.Content);

            await File.AppendAllTextAsync(
                _filePath,
                JsonSerializer.Serialize(record) + Environment.NewLine,
                cancellationToken);

            var info = new FileInfo(_filePath);
            if (info.Length > CompactThresholdBytes)
                await CompactAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CompactAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        var retained = lines.TakeLast(RetainedMessagesAfterCompaction);
        var temporaryPath = _filePath + ".tmp";
        await File.WriteAllLinesAsync(temporaryPath, retained, cancellationToken);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static bool IsPersistableRole(string? role) =>
        string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase);

    private sealed record StoredMessage(
        DateTimeOffset Timestamp,
        string Role,
        string Content);
}
