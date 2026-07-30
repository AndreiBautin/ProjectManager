namespace ProjectManager.Api.Models;

public class ActionItem
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Description { get; set; } = string.Empty;
    public ActionStatus Status { get; set; } = ActionStatus.Pending;
    public int Order { get; set; }

    // Null = doable anytime (ASAP). Set = this action isn't eligible to be
    // "the next thing to work on" until this date arrives (e.g. a scheduled
    // appointment). It still displays normally either way - this only gates
    // recommendation/eligibility, never visibility.
    public DateTime? AvailableFrom { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedDate { get; set; }
}
