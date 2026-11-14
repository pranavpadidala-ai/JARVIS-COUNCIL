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
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            string.IsNullOrWhiteSpace(local) ? AppContext.BaseDirectory : local, "JARVIS", "checkpoints"));
    }

    public async Task SaveAsync(TaskCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Validate(checkpoint);
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, checkpoint.TaskId.ToString("N") + ".json");
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
        var path = Path.Combine(_root, taskId.ToString("N") + ".json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumCheckpointBytes)
            throw new InvalidDataException("Checkpoint exceeds the 2 MB safety limit.");
        await using var stream = File.OpenRead(path);
        var checkpoint = await JsonSerializer.DeserializeAsync<TaskCheckpoint>(stream, _options, cancellationToken);
        if (checkpoint is null) return null;
        Validate(checkpoint);
        if (checkpoint.TaskId != taskId)
            throw new InvalidDataException("Checkpoint task ID does not match its file name.");
        return checkpoint;
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

        var current = fullPath;
        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Checkpoint artifacts cannot traverse symbolic links or reparse points.");
            current = Directory.GetParent(current)?.FullName
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

    private static void Validate(TaskCheckpoint checkpoint)
    {
        if (checkpoint.SchemaVersion != 1 || checkpoint.TaskId == Guid.Empty ||
            checkpoint.RetryCount < 0 || string.IsNullOrWhiteSpace(checkpoint.IdempotencyKey))
            throw new InvalidDataException("Checkpoint metadata is invalid.");
        if (checkpoint.CompletedSteps is null || checkpoint.Artifacts is null ||
            checkpoint.CompletedSteps.Count > 500 || checkpoint.Artifacts.Count > 500 ||
            (checkpoint.Objective?.Length ?? 0) > 20000 || (checkpoint.NextAction?.Length ?? 0) > 20000)
            throw new InvalidDataException("Checkpoint content exceeds safety limits.");
        foreach (var artifact in checkpoint.Artifacts)
        {
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.RelativePath) || Path.IsPathRooted(artifact.RelativePath) ||
                artifact.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p == "..") ||
                string.IsNullOrWhiteSpace(artifact.Sha256) || artifact.Sha256.Length != 64 || artifact.Sha256.Any(c => !Uri.IsHexDigit(c)) || artifact.Length < 0)
                throw new InvalidDataException("Checkpoint contains an invalid artifact reference.");
        }
    }
}