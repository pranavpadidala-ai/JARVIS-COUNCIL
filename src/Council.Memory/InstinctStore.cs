using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Council.Security;

namespace Council.Memory;

/// <summary>
/// User-approved, workspace-scoped learned practices. Nothing is inferred or persisted automatically.
/// </summary>
public sealed class InstinctStore
{
    private const int MaxInstincts = 100;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InstinctStore(string workspaceRoot, string? storageRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var canonicalRoot = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var projectId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRoot)))[..12].ToLowerInvariant();

        var root = storageRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(root, "JARVIS-Council", "Instincts");
        }

        _filePath = Path.Combine(Path.GetFullPath(root), projectId + ".jsonl");
    }

    public async Task<LearnedInstinct> AddOrReinforceAsync(
        string trigger,
        string action,
        string domain = "general",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        trigger = trigger.Trim();
        action = action.Trim();
        domain = domain.Trim().ToLowerInvariant();

        if (trigger.Length > 160) throw new ArgumentOutOfRangeException(nameof(trigger), "Trigger must be 160 characters or fewer.");
        if (action.Length > 1200) throw new ArgumentOutOfRangeException(nameof(action), "Action must be 1,200 characters or fewer.");
        if (domain.Length > 48) throw new ArgumentOutOfRangeException(nameof(domain), "Domain must be 48 characters or fewer.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadUnsafeAsync(cancellationToken);
            var existingIndex = items.FindIndex(item =>
                string.Equals(item.Domain, domain, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Normalize(item.Trigger), Normalize(trigger), StringComparison.Ordinal));
            LearnedInstinct saved;

            if (existingIndex >= 0)
            {
                var existing = items[existingIndex];
                saved = existing with
                {
                    Action = SecretRedactor.Redact(action),
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Confidence = Math.Min(0.95, Math.Round(existing.Confidence + 0.05, 2)),
                    EvidenceCount = existing.EvidenceCount + 1
                };
                items[existingIndex] = saved;
            }
            else
            {
                if (items.Count >= MaxInstincts)
                    throw new InvalidOperationException($"Project learning is limited to {MaxInstincts} practices. Remove an old practice first.");

                saved = new LearnedInstinct(
                    Guid.NewGuid().ToString("N")[..8],
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    SecretRedactor.Redact(trigger),
                    SecretRedactor.Redact(action),
                    domain,
                    0.60,
                    1);
                items.Add(saved);
            }

            await WriteUnsafeAsync(items, cancellationToken);
            return saved;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<LearnedInstinct>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadUnsafeAsync(cancellationToken))
                .OrderByDescending(item => item.Confidence)
                .ThenByDescending(item => item.UpdatedAt)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadUnsafeAsync(cancellationToken);
            var removed = items.RemoveAll(item =>
                string.Equals(item.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) await WriteUnsafeAsync(items, cancellationToken);
            return removed;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<LearnedInstinct>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return [];
        var items = new List<LearnedInstinct>();
        foreach (var line in await File.ReadAllLinesAsync(_filePath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var stored = JsonSerializer.Deserialize<StoredInstinct>(line);
                if (stored is null) continue;
                var json = LocalMemoryProtector.Unprotect(stored.Payload);
                var item = JsonSerializer.Deserialize<LearnedInstinct>(json);
                if (item is not null && item.Confidence is >= 0.3 and <= 0.95 && item.EvidenceCount > 0)
                    items.Add(item);
            }
            catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
            {
                // Ignore damaged or unreadable records and continue.
            }
        }
        return items;
    }

    private async Task WriteUnsafeAsync(IReadOnlyCollection<LearnedInstinct> items, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var lines = items.Select(item => JsonSerializer.Serialize(new StoredInstinct(
            LocalMemoryProtector.Protect(JsonSerializer.Serialize(item)))));
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllLinesAsync(temporaryPath, lines, cancellationToken);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private sealed record StoredInstinct(string Payload);
}

public sealed record LearnedInstinct(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Trigger,
    string Action,
    string Domain,
    double Confidence,
    int EvidenceCount);
