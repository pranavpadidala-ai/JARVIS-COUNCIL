namespace Council.Dev.Recovery;

public sealed record TaskArtifactSnapshot(string RelativePath, string Sha256, long Length);

public sealed record TaskCheckpoint
{
    public int SchemaVersion { get; init; } = 1;
    public Guid TaskId { get; init; }
    public string Objective { get; init; } = string.Empty;
    public IReadOnlyList<string> CompletedSteps { get; init; } = [];
    public string NextAction { get; init; } = string.Empty;
    public IReadOnlyList<TaskArtifactSnapshot> Artifacts { get; init; } = [];
    public string? ProviderRoute { get; init; }
    public string? ModelAlias { get; init; }
    public string? GitCommitSha { get; init; }
    public string? GitWorkingTreeStatus { get; init; }
    public string? BuildLogPath { get; init; }
    public string? TestLogPath { get; init; }
    public int RetryCount { get; init; }
    public string? FailureClassification { get; init; }
    public string IdempotencyKey { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
}