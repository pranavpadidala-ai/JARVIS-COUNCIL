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

    /// <summary>Only tasks explicitly approved by the user may be claimed by the background agent.</summary>
    public bool IsApproved { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>Approval is one-shot; failed tasks need a fresh user approval before background retry.</summary>
    public DateTime? ApprovalConsumedAtUtc { get; set; }

    public string CurrentStep { get; set; } = string.Empty;
}
