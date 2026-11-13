using System.Text.Json;
using Council.Core.Models;

namespace Council.App.Chat;

/// <summary>
/// Stores chat history locally under the current Windows user profile.
/// Provider credentials are never written to this file.
/// </summary>
public sealed class ConversationStore
{
    private const int MaxStoredMessages = 120;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true
    };

    public ConversationStore(string? filePath = null)
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(
            string.IsNullOrWhiteSpace(localData) ? AppContext.BaseDirectory : localData,
            "JARVIS",
            "Conversations");

        _filePath = filePath ?? Path.Combine(directory, "current.json");
    }

    public async Task<List<AIMessage>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var stream = File.OpenRead(_filePath);
        var messages = await JsonSerializer.DeserializeAsync<List<AIMessage>>(
            stream,
            _options,
            cancellationToken);

        return messages?.TakeLast(MaxStoredMessages).ToList() ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyCollection<AIMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Conversation path is invalid.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    messages.TakeLast(MaxStoredMessages).ToList(),
                    _options,
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

    public Task ClearAsync()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
        return Task.CompletedTask;
    }
}
