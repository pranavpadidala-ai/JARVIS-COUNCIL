using System.Text.Json;

namespace Council.App.Phone;

public enum PhoneCommandStatus
{
    Queued,
    Processing,
    Cancelled,
    Completed,
    Failed
}

public sealed record PhoneCommand(
    Guid Id,
    string RequestId,
    string Text,
    PhoneCommandStatus Status,
    DateTime CreatedAtUtc,
    string? Result = null);

/// <summary>
/// Durable phone inbox shared by the desktop and console sessions. Updates are
/// serialized both in-process and across processes; queued commands must be
/// explicitly claimed before execution.
/// </summary>
public sealed class PhoneCommandInbox
{
    private readonly string _path;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PhoneCommandInbox(string? path = null)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        _path = Path.GetFullPath(path ?? Path.Combine(
            localData, "JARVIS-Council", "PhoneBridge", "inbox.json"));
        _lockPath = _path + ".lock";
    }

    public async Task<PhoneCommand> EnqueueAsync(
        string requestId,
        string text,
        CancellationToken cancellationToken = default)
    {
        requestId = requestId?.Trim() ?? string.Empty;
        text = text?.Trim() ?? string.Empty;
        if (requestId.Length is < 8 or > 80)
            throw new ArgumentException("Request ID must contain 8 to 80 characters.", nameof(requestId));
        if (text.Length is < 1 or > 2000)
            throw new ArgumentException("Command text must contain 1 to 2000 characters.", nameof(text));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            var items = await LoadCoreAsync(cancellationToken);
            var existing = items.FirstOrDefault(x => string.Equals(x.RequestId, requestId, StringComparison.Ordinal));
            if (existing is not null)
                return existing;

            if (items.Count(x => x.Status is PhoneCommandStatus.Queued or PhoneCommandStatus.Processing) >= 100)
                throw new InvalidOperationException("The phone command queue is full.");

            var item = new PhoneCommand(Guid.NewGuid(), requestId, text, PhoneCommandStatus.Queued, DateTime.UtcNow);
            items.Add(item);
            await SaveCoreAsync(items, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PhoneCommand>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PhoneCommand>> ListRecentAsync(
        int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        if (maxItems is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Recent history limit must be between 1 and 500.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            return (await LoadCoreAsync(cancellationToken)).TakeLast(maxItems).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PhoneCommand?> GetNextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            return (await LoadCoreAsync(cancellationToken))
                .FirstOrDefault(x => x.Status == PhoneCommandStatus.Queued);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Atomically claims a queued command so only one JARVIS session can execute it.</summary>
    public async Task<PhoneCommand?> ClaimAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            var items = await LoadCoreAsync(cancellationToken);
            var index = items.FindIndex(x => x.Id == id && x.Status == PhoneCommandStatus.Queued);
            if (index < 0)
                return null;

            items[index] = items[index] with { Status = PhoneCommandStatus.Processing };
            await SaveCoreAsync(items, cancellationToken);
            return items[index];
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> CompleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(id, PhoneCommandStatus.Processing, PhoneCommandStatus.Completed, null, cancellationToken);

    public Task<bool> CompleteAsync(Guid id, string result, CancellationToken cancellationToken = default)
    {
        result = result?.Trim() ?? string.Empty;
        if (result.Length > 12_000)
            result = result[..12_000] + Environment.NewLine + "[Result truncated by JARVIS phone bridge.]";
        return UpdateStatusAsync(id, PhoneCommandStatus.Processing, PhoneCommandStatus.Completed, result, cancellationToken);
    }

    public Task<bool> FailAsync(Guid id, string result, CancellationToken cancellationToken = default)
    {
        result = result?.Trim() ?? "JARVIS could not complete this request.";
        if (result.Length > 12_000)
            result = result[..12_000] + Environment.NewLine + "[Result truncated by JARVIS phone bridge.]";
        return UpdateStatusAsync(id, PhoneCommandStatus.Processing, PhoneCommandStatus.Failed, result, cancellationToken);
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        UpdateStatusAsync(id, PhoneCommandStatus.Queued, PhoneCommandStatus.Cancelled, null, cancellationToken);

    private async Task<bool> UpdateStatusAsync(
        Guid id,
        PhoneCommandStatus expectedStatus,
        PhoneCommandStatus status,
        string? result,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            var items = await LoadCoreAsync(cancellationToken);
            var index = items.FindIndex(x => x.Id == id && x.Status == expectedStatus);
            if (index < 0)
                return false;

            items[index] = items[index] with { Status = status, Result = result };
            await SaveCoreAsync(items, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_lockPath);
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

    private async Task<List<PhoneCommand>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return [];

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<PhoneCommand>>(
            stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task SaveCoreAsync(List<PhoneCommand> items, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
