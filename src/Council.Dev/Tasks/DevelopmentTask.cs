using Council.Dev.Recovery;

namespace Council.Dev.Tasks;

public sealed class DevelopmentTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int AttemptCount { get; set; }
    public bool IsApproved { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ApprovalConsumedAtUtc { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public List<string> CompletedSteps { get; set; } = [];
    public string? ProviderRoute { get; set; }
    public string? ModelAlias { get; set; }
    public string? GitCommitSha { get; set; }
    public string? GitWorkingTreeStatus { get; set; }
    public string? BuildLogPath { get; set; }
    public string? TestLogPath { get; set; }
    public string? FailureClassification { get; set; }
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
    public List<TaskArtifactSnapshot> Artifacts { get; set; } = [];
}