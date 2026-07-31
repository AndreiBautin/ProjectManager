using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Dtos;
using ProjectManager.Api.Models;

namespace ProjectManager.Api.Controllers;

[ApiController]
public class ActionsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ActionsController(AppDbContext db) => _db = db;

    [HttpPost("api/projects/{projectId}/actions")]
    public async Task<ActionResult<ActionDto>> Create(int projectId, CreateActionRequest request)
    {
        var project = await _db.Projects.Include(p => p.Actions).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return NotFound("Project not found.");

        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Action description is required.");

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
            .Include(a => a.Project)
            .ThenInclude(p => p!.Actions)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (action == null) return NotFound();

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
                project.Progress = 100;
                project.CompletedDate = DateTime.UtcNow;
            }
            else if (!allActionsDone && project.Status == ProjectStatus.Completed)
            {
                // Reopening an action (e.g. unchecking it by mistake) un-completes
                // the project rather than leaving it Completed with pending work.
                project.Status = project.IsBlocked ? ProjectStatus.Blocked : ProjectStatus.Active;
                project.CompletedDate = null;
            }
        }

        await _db.SaveChangesAsync();
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
