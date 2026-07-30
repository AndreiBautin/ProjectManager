using ProjectManager.Api.Models;
using ProjectManager.Api.Services;

namespace ProjectManager.Api.Dtos;

public static class Mapping
{
    public static ActionDto ToDto(this ActionItem a) => new(
        a.Id, a.ProjectId, a.Description, a.Status.ToString(), a.Order,
        a.AvailableFrom, PriorityEngine.IsEligibleNow(a), a.CreatedDate, a.CompletedDate);

    public static ProjectDto ToDto(this Project p)
    {
        var nextAction = PriorityEngine.GetCurrentNextAction(p);
        return new ProjectDto(
            p.Id,
            p.Name,
            p.Description,
            p.CategoryId,
            p.Category?.Name,
            p.Impact,
            p.Urgency,
            p.Effort,
            PriorityEngine.ComputeScore(p),
            p.Status.ToString(),
            p.Progress,
            p.IsBlocked,
            p.BlockReason,
            p.Deadline,
            p.CreatedDate,
            p.UpdatedDate,
            p.CompletedDate,
            nextAction?.ToDto(),
            p.Actions.OrderBy(a => a.Order).Select(a => a.ToDto()).ToList());
    }
}
