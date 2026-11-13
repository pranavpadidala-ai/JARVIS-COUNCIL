using System.Security.Cryptography;
using System.Text.Json;

namespace Council.Dev.Recovery;

public sealed class TaskCheckpointStore
{
    private const int MaximumCheckpointBytes = 2 * 1024 * 1024;
    private readonly string _root;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public TaskCheckpointStore(string? rootDirectory = null)
    {
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JARVIS", "checkpoints"));
    }

    public async Task SaveAsync(TaskCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Validate(checkpoint);
        Directory.CreateDirectory(_root);
        var target = GetPath(checkpoint.TaskId);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
                if (stream.Length > MaximumCheckpointBytes)
                    throw new InvalidOperationException("Checkpoint exceeds the 2 MB safety limit.");
            }
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<TaskCheckpoint?> LoadAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(taskId);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length > MaximumCheckpointBytes)
            throw new InvalidDataException("Checkpoint exceeds the 2 MB safety limit.");
        await using var stream = File.OpenRead(path);
        var checkpoint = await JsonSerializer.DeserializeAsync<TaskCheckpoint>(stream, _options, cancellationToken);
        if (checkpoint is null) return null;
        Validate(checkpoint);
        if (checkpoint.TaskId != taskId)
            throw new InvalidDataException("Checkpoint task ID does not match its file name.");
        return checkpoint;
    }

    public async Task<IReadOnlyList<TaskCheckpoint>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root)) return [];
        var result = new List<TaskCheckpoint>();
        foreach (var file in Directory.EnumerateFiles(_root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (info.Length > MaximumCheckpointBytes) continue;
            await using var stream = File.OpenRead(file);
            var checkpoint = await JsonSerializer.DeserializeAsync<TaskCheckpoint>(stream, _options, cancellationToken);
            if (checkpoint is not null)
            {
                Validate(checkpoint);
                result.Add(checkpoint);
            }
        }
        return result;
    }

    public static async Task<TaskArtifactSnapshot> HashArtifactAsync(
        string workspaceRoot, string filePath, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var fullPath = Path.GetFullPath(filePath, root);
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidOperationException("Artifact must be inside the task workspace.");
        var currentPath = fullPath;
        while (!string.Equals(currentPath, root, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.Exists(currentPath) || Directory.Exists(currentPath)) &&
                (File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Checkpoint artifacts cannot traverse symbolic links or reparse points.");
            currentPath = Directory.GetParent(currentPath)?.FullName
                ?? throw new InvalidOperationException("Artifact path could not be safely resolved.");
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Checkpoint artifact was not found.", fullPath);
        if (info.Length > 25 * 1024 * 1024)
            throw new InvalidOperationException("Checkpoint artifacts are limited to 25 MB each.");
        await using var stream = File.OpenRead(fullPath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new TaskArtifactSnapshot(relative, Convert.ToHexString(hash), info.Length);
    }

    private string GetPath(Guid taskId) => Path.Combine(_root, taskId.ToString("N") + ".json");

    private static void Validate(TaskCheckpoint checkpoint)
    {
        if (checkpoint.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported checkpoint schema version: {checkpoint.SchemaVersion}.");
        if (checkpoint.TaskId == Guid.Empty)
            throw new InvalidDataException("Checkpoint task ID is required.");
        if (checkpoint.RetryCount < 0)
            throw new InvalidDataException("Checkpoint retry count cannot be negative.");
        if (string.IsNullOrWhiteSpace(checkpoint.IdempotencyKey))
            throw new InvalidDataException("Checkpoint idempotency key is required.");
        if (checkpoint.AcceptanceCriteria is null || checkpoint.CompletedSteps is null || checkpoint.Artifacts is null)
            throw new InvalidDataException("Checkpoint collections cannot be null.");
        if (checkpoint.AcceptanceCriteria.Count > 100 || checkpoint.CompletedSteps.Count > 500 ||
            checkpoint.Artifacts.Count > 500)
            throw new InvalidDataException("Checkpoint contains too many entries.");
        if (checkpoint.Objective.Length > 20000 || checkpoint.NextAction.Length > 20000)
            throw new InvalidDataException("Checkpoint text exceeds the safety limit.");
        foreach (var artifact in checkpoint.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.RelativePath) || Path.IsPathRooted(artifact.RelativePath) ||
                artifact.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == "..") ||
                artifact.Sha256.Length != 64 || artifact.Sha256.Any(ch => !Uri.IsHexDigit(ch)) || artifact.Length < 0)
                throw new InvalidDataException("Checkpoint contains an invalid artifact reference.");
        }
        // Credentials and browser auth state are intentionally not represented by this schema.
    }
}