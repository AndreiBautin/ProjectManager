using ProjectManager.Api.Models;

namespace ProjectManager.Api.Services;

public record RecommendationResult(
    int? ProjectId,
    string? ProjectName,
    int? ActionId,
    string? ActionDescription,
    string Reason);

public static class PriorityEngine
{
    // How many days out a Deadline starts pulling effective urgency toward
    // 10. Fixed rather than proportional to how far out the deadline
    // originally was - it works the same whether the deadline was 2 weeks
    // or 6 months away, since it only ever kicks in once things get close.
    private const int DeadlineRampDays = 14;

    public static int ComputeScore(Project project)
    {
        var effort = Math.Max(project.Effort, 1);
        var effectiveUrgency = ComputeEffectiveUrgency(project);
        return (int)Math.Round((project.Impact * effectiveUrgency) / effort);
    }

    /// <summary>
    /// No Deadline: urgency is exactly what was manually set - no change from
    /// today's behavior. With a Deadline: urgency ramps linearly from the
    /// manual value up to 10 over the last DeadlineRampDays before it, and
    /// is pinned at 10 once due or overdue. This only ever pushes urgency
    /// up, never down - a manually-set 10 is unaffected, and a task that
    /// feels minor day-to-day still gets forced to the top as a hard
    /// external deadline actually arrives.
    /// </summary>
    public static double ComputeEffectiveUrgency(Project project)
    {
        if (project.Deadline == null) return project.Urgency;

        var daysRemaining = (project.Deadline.Value.Date - DateTime.Now.Date).TotalDays;
        if (daysRemaining <= 0) return 10;
        if (daysRemaining >= DeadlineRampDays) return project.Urgency;

        var progress = (DeadlineRampDays - daysRemaining) / DeadlineRampDays;
        return project.Urgency + (10 - project.Urgency) * progress;
    }

    public static ActionItem? GetCurrentNextAction(Project project)
    {
        return project.Actions
            .Where(a => a.Status == ActionStatus.Pending)
            .OrderBy(a => a.Order)
            .FirstOrDefault();
    }

    /// <summary>
    /// An action with no AvailableFrom date is doable anytime. One with a
    /// future date isn't eligible to be worked on / recommended yet - it's
    /// still visible, just not actionable today (e.g. a scheduled appointment).
    /// Uses local time deliberately: this is a single-user app that always
    /// runs on the person's own machine, so "today" should mean their day.
    /// </summary>
    public static bool IsEligibleNow(ActionItem? action)
    {
        if (action == null) return false;
        return action.AvailableFrom == null || action.AvailableFrom.Value.Date <= DateTime.Now.Date;
    }

    /// <summary>
    /// Ranks all non-Paused, non-Completed projects by priority score (desc),
    /// then urgency (desc), then created date (asc) as a tiebreaker.
    /// </summary>
    public static List<Project> RankActiveProjects(IEnumerable<Project> projects)
    {
        return projects
            .Where(p => p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Blocked)
            .OrderByDescending(ComputeScore)
            .ThenByDescending(ComputeEffectiveUrgency)
            .ThenBy(p => p.CreatedDate)
            .ToList();
    }

    /// <summary>
    /// Walks the ranked list and picks the single recommended next action:
    /// - the top Active project's next action, or
    /// - if the top project is Blocked, its next action IS the unblock action
    ///   (recommend it, since resolving it unlocks a high-value project), or
    /// - if a project's next action isn't eligible yet - either undefined, or
    ///   defined but date-gated in the future (e.g. waiting on a scheduled
    ///   appointment) - it's not actionable today, so skip it and move to the
    ///   next ranked project.
    /// </summary>
    public static RecommendationResult GetRecommendation(IEnumerable<Project> projects)
    {
        var ranked = RankActiveProjects(projects);

        foreach (var project in ranked)
        {
            var nextAction = GetCurrentNextAction(project);
            if (!IsEligibleNow(nextAction))
            {
                // No next action defined, or the only one isn't available yet - skip.
                continue;
            }

            var reason = project.Status == ProjectStatus.Blocked
                ? "Unblocks a high-priority project"
                : "Highest priority active project";

            return new RecommendationResult(
                project.Id, project.Name, nextAction!.Id, nextAction.Description, reason);
        }

        return new RecommendationResult(
            null, null, null, null,
            "No actionable projects right now. Add a next action to a blocked project, add a new project, or check back once a waiting item's date arrives.");
    }
}
