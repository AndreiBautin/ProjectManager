namespace ProjectManager.Api.Models;

// Self-referencing join: ProjectId is the project that is stuck, BlockingProjectId
// is the project that must complete first. Kept separate from IsBlocked/BlockReason,
// which cover blocks that aren't other tracked projects (e.g. "waiting on the DMV").
public class ProjectBlocker
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int BlockingProjectId { get; set; }
    public Project? BlockingProject { get; set; }
}
