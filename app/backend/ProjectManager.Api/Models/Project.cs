namespace ProjectManager.Api.Models;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int Impact { get; set; } = 5;
    public int Urgency { get; set; } = 5;
    public int Effort { get; set; } = 5;

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public int Progress { get; set; } = 0;

    public bool IsBlocked { get; set; } = false;
    public string? BlockReason { get; set; }

    // Optional. Null = no deadline, Urgency is used as-is (the common case).
    // When set, effective urgency ramps toward 10 over the last 14 days
    // before this date - see PriorityEngine.ComputeEffectiveUrgency.
    public DateTime? Deadline { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedDate { get; set; }

    public List<ActionItem> Actions { get; set; } = new();
}
