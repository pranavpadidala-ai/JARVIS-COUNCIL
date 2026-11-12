using System.Text.Json;
using System.Security.Cryptography;
using Council.Security;

namespace Council.Memory;

/// <summary>
/// Explicit, user-managed long-term facts. Facts are encrypted at rest and
/// are never inferred or saved automatically.
/// </summary>
public sealed class MemoryFactStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private const int MaxFacts = 100;

    public MemoryFactStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<string> AddAsync(string fact, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fact);
        var normalized = fact.Trim();
        if (normalized.Length > 2000)
            throw new ArgumentOutOfRangeException(nameof(fact), "A saved fact must be 2,000 characters or fewer.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            if (File.Exists(_filePath))
            {
                var current = await ListCountAsync(cancellationToken);
                if (current >= MaxFacts)
                    throw new InvalidOperationException($"Long-term memory is limited to {MaxFacts} facts. Remove an old fact first.");
            }
            var entry = new StoredFact(Guid.NewGuid().ToString("N")[..8],
                DateTimeOffset.UtcNow, LocalMemoryProtector.Protect(normalized));
            await File.AppendAllTextAsync(_filePath,
                JsonSerializer.Serialize(entry) + Environment.NewLine, cancellationToken);
            return entry.Id;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<(string Id, DateTimeOffset CreatedAt, string Fact)>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<(string, DateTimeOffset, string)>();

            var result = new List<(string, DateTimeOffset, string)>();
            foreach (var line in await File.ReadAllLinesAsync(_filePath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<StoredFact>(line);
                    if (entry is null) continue;
                    result.Add((entry.Id, entry.CreatedAt,
                        SecretRedactor.Redact(LocalMemoryProtector.Unprotect(entry.Fact))));
                }
                catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException) { /* Skip damaged or unreadable records. */ }
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath)) return false;
            var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
            var retained = new List<string>(lines.Length);
            var removed = false;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<StoredFact>(line);
                    if (entry is not null && string.Equals(entry.Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        removed = true;
                        continue;
                    }
                }
                catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException) { }
                retained.Add(line);
            }
            if (removed)
            {
                var temp = _filePath + ".tmp";
                await File.WriteAllLinesAsync(temp, retained, cancellationToken);
                File.Move(temp, _filePath, overwrite: true);
            }
            return removed;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { if (File.Exists(_filePath)) File.Delete(_filePath); }
        finally { _gate.Release(); }
    }

    private async Task<int> ListCountAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var line in await File.ReadAllLinesAsync(_filePath, cancellationToken))
        {
            try
            {
                if (JsonSerializer.Deserialize<StoredFact>(line) is not null) count++;
            }
            catch (JsonException) { }
        }
        return count;
    }

    private sealed record StoredFact(string Id, DateTimeOffset CreatedAt, string Fact);
}
