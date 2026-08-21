using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Dtos;
using ProjectManager.Api.Models;
using ProjectManager.Api.Services;
using ProjectManager.Api.Validation;

namespace ProjectManager.Api.Controllers;

[ApiController]
public class ActionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BlockingService _blocking;
    public ActionsController(AppDbContext db, BlockingService blocking)
    {
        _db = db;
        _blocking = blocking;
    }

    [HttpPost("api/projects/{projectId}/actions")]
    public async Task<ActionResult<ActionDto>> Create(int projectId, CreateActionRequest request)
    {
        var project = await _db.Projects.Include(p => p.Actions).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return NotFound("Project not found.");

        var validationErrors = RequestValidator.ValidateCreateAction(request);
        if (validationErrors.Count > 0) return BadRequest(string.Join(" ", validationErrors));

        var order = request.Order ?? (project.Actions.Count == 0 ? 1 : project.Actions.Max(a => a.Order) + 1);

        var action = new ActionItem
        {
            ProjectId = projectId,
            Description = request.Description.Trim(),
            Status = ActionStatus.Pending,
            Order = order,
            AvailableFrom = request.AvailableFrom,
            CreatedDate = DateTime.UtcNow
        };

        _db.Actions.Add(action);
        project.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(action.ToDto());
    }

    [HttpPut("api/actions/{id}")]
    public async Task<ActionResult<ActionDto>> Update(int id, UpdateActionRequest request)
    {
        var action = await _db.Actions
            .Include(a => a.Project).ThenInclude(p => p!.Actions)
            .Include(a => a.Project).ThenInclude(p => p!.Blockers).ThenInclude(b => b.BlockingProject)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (action == null) return NotFound();

        var validationErrors = RequestValidator.ValidateUpdateAction(request);
        if (validationErrors.Count > 0) return BadRequest(string.Join(" ", validationErrors));

        if (!string.IsNullOrWhiteSpace(request.Description))
            action.Description = request.Description.Trim();

        if (request.Order.HasValue)
            action.Order = request.Order.Value;

        if (request.ClearAvailableFrom)
            action.AvailableFrom = null;
        else if (request.AvailableFrom.HasValue)
            action.AvailableFrom = request.AvailableFrom;

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<ActionStatus>(request.Status, true, out var parsedStatus))
                return BadRequest("Invalid action status.");

            action.Status = parsedStatus;
            action.CompletedDate = parsedStatus == ActionStatus.Done ? DateTime.UtcNow : null;
        }

        var project = action.Project;
        var wasCompleted = project?.Status == ProjectStatus.Completed;

        if (project != null)
        {
            project.UpdatedDate = DateTime.UtcNow;

            // Finishing the last pending action closes the project out - there's
            // nothing left to recommend, so "next suggested task" would otherwise
            // point at a project that's actually done.
            var allActionsDone = project.Actions.Count > 0 && project.Actions.All(a => a.Status == ActionStatus.Done);
            if (allActionsDone && project.Status != ProjectStatus.Completed)
            {
                project.Status = ProjectStatus.Completed;
                project.CompletedDate = DateTime.UtcNow;
            }
            else if (!allActionsDone && project.Status == ProjectStatus.Completed)
            {
                // Reopening an action (e.g. unchecking it by mistake) un-completes
                // the project rather than leaving it Completed with pending work.
                project.Status = PriorityEngine.DeriveStatus(project, ProjectStatus.Active);
                project.CompletedDate = null;
            }
        }

        await _db.SaveChangesAsync();

        // A Completed <-> non-Completed transition here can unblock (or re-block)
        // whatever else was waiting on this project.
        if (project != null && wasCompleted != (project.Status == ProjectStatus.Completed))
        {
            await _blocking.RecomputeDependentsAsync(project.Id);
        }

        return Ok(action.ToDto());
    }

    [HttpDelete("api/actions/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var action = await _db.Actions.FindAsync(id);
        if (action == null) return NotFound();

        _db.Actions.Remove(action);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
