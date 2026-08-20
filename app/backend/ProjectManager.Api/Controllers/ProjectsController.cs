using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Dtos;
using ProjectManager.Api.Models;
using ProjectManager.Api.Services;

namespace ProjectManager.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BlockingService _blocking;
    public ProjectsController(AppDbContext db, BlockingService blocking)
    {
        _db = db;
        _blocking = blocking;
    }

    private IQueryable<Project> ProjectsWithIncludes() =>
        _db.Projects.Include(p => p.Category).Include(p => p.Actions)
            .Include(p => p.Blockers).ThenInclude(b => b.BlockingProject);

    // GET /api/projects?status=Active,Blocked
    // Default (no filter): everything except Completed.
    [HttpGet]
    public async Task<ActionResult<List<ProjectDto>>> GetAll([FromQuery] string? status)
    {
        var query = ProjectsWithIncludes().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<ProjectStatus>(s, true, out var parsed) ? parsed : (ProjectStatus?)null)
                .Where(s => s != null)
                .Select(s => s!.Value)
                .ToHashSet();

            query = query.Where(p => wanted.Contains(p.Status));
        }
        else
        {
            query = query.Where(p => p.Status != ProjectStatus.Completed);
        }

        var projects = await query.ToListAsync();

        var ranked = PriorityEngine.RankActiveProjects(projects);
        var rankedIds = ranked.Select((p, i) => (p.Id, Rank: i)).ToDictionary(x => x.Id, x => x.Rank);

        var dtos = projects
            .Select(p => p.ToDto())
            .OrderBy(d => rankedIds.TryGetValue(d.Id, out var r) ? r : int.MaxValue)
            .ThenByDescending(d => d.CreatedDate)
            .ToList();

        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectDto>> GetById(int id)
    {
        var project = await ProjectsWithIncludes().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();
        return Ok(project.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Project name is required.");

        var blockerIds = (request.BlockedByProjectIds ?? new List<int>()).Distinct().ToList();
        var blockerError = await _blocking.ValidateBlockersAsync(0, blockerIds);
        if (blockerError != null) return BadRequest(blockerError);

        int? categoryId = request.CategoryId;
        if (categoryId == null && !string.IsNullOrWhiteSpace(request.NewCategoryName))
        {
            var name = request.NewCategoryName.Trim();
            var existingCategory = await _db.Categories
                .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
            if (existingCategory == null)
            {
                existingCategory = new Category { Name = name };
                _db.Categories.Add(existingCategory);
                await _db.SaveChangesAsync();
            }
            categoryId = existingCategory.Id;
        }

        var blockedByOpenProjects = blockerIds.Count > 0 && await _db.Projects
            .Where(p => blockerIds.Contains(p.Id) && p.Status != ProjectStatus.Completed)
            .AnyAsync();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            CategoryId = categoryId,
            Impact = Clamp(request.Impact),
            Urgency = Clamp(request.Urgency),
            Effort = Clamp(request.Effort),
            IsBlocked = request.IsBlocked,
            BlockReason = request.IsBlocked ? request.BlockReason : null,
            Deadline = request.Deadline,
            Status = (request.IsBlocked || blockedByOpenProjects) ? ProjectStatus.Blocked : ProjectStatus.Active,
            CreatedDate = now,
            UpdatedDate = now
        };

        foreach (var blockerId in blockerIds)
        {
            project.Blockers.Add(new ProjectBlocker { BlockingProjectId = blockerId });
        }

        if (!string.IsNullOrWhiteSpace(request.FirstActionDescription))
        {
            project.Actions.Add(new ActionItem
            {
                Description = request.FirstActionDescription.Trim(),
                Status = ActionStatus.Pending,
                Order = 1,
                CreatedDate = now
            });
        }

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var created = await ProjectsWithIncludes().FirstAsync(p => p.Id == project.Id);
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, created.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ProjectDto>> Update(int id, UpdateProjectRequest request)
    {
        var project = await ProjectsWithIncludes().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        if (!Enum.TryParse<ProjectStatus>(request.Status, true, out var requestedStatus))
            return BadRequest("Invalid status value.");

        var blockerIds = (request.BlockedByProjectIds ?? new List<int>()).Distinct().ToList();
        var blockerError = await _blocking.ValidateBlockersAsync(id, blockerIds);
        if (blockerError != null) return BadRequest(blockerError);

        project.Name = request.Name.Trim();
        project.Description = request.Description;
        project.CategoryId = request.CategoryId;
        project.Impact = Clamp(request.Impact);
        project.Urgency = Clamp(request.Urgency);
        project.Effort = Clamp(request.Effort);
        project.IsBlocked = request.IsBlocked;
        project.BlockReason = request.IsBlocked ? request.BlockReason : null;
        project.Deadline = request.Deadline;

        _blocking.SyncBlockers(project, blockerIds);

        var blockedByOpenProjects = blockerIds.Count > 0 && await _db.Projects
            .Where(p => blockerIds.Contains(p.Id) && p.Status != ProjectStatus.Completed)
            .AnyAsync();

        // Status derivation: IsBlocked/blocked-by-other-projects and Status must
        // agree, except Completed/Paused are explicit user choices that take
        // precedence over blocked-derivation.
        if (requestedStatus == ProjectStatus.Completed || requestedStatus == ProjectStatus.Paused)
        {
            project.Status = requestedStatus;
        }
        else
        {
            project.Status = (project.IsBlocked || blockedByOpenProjects) ? ProjectStatus.Blocked : ProjectStatus.Active;
        }

        if (project.Status == ProjectStatus.Completed && project.CompletedDate == null)
            project.CompletedDate = DateTime.UtcNow;
        else if (project.Status != ProjectStatus.Completed)
            project.CompletedDate = null;

        project.UpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Cheap enough for a single-user local app to run unconditionally - this
        // is what lets a dependent project auto-flip back to Active once every
        // project blocking it (this one included) reaches Completed.
        await _blocking.RecomputeDependentsAsync(id);

        var updated = await ProjectsWithIncludes().FirstAsync(p => p.Id == id);
        return Ok(updated.ToDto());
    }

    [HttpPost("{id}/complete")]
    public async Task<ActionResult<ProjectDto>> Complete(int id)
    {
        var project = await ProjectsWithIncludes().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        project.Status = ProjectStatus.Completed;
        project.CompletedDate = DateTime.UtcNow;
        project.UpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _blocking.RecomputeDependentsAsync(id);

        var updated = await ProjectsWithIncludes().FirstAsync(p => p.Id == id);
        return Ok(updated.ToDto());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return NotFound();

        // Capture dependents before deleting - the join rows referencing this
        // project are gone via cascade delete once it's removed.
        var dependentIds = await _blocking.GetDependentIdsAsync(id);

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();

        await _blocking.RecomputeAsync(dependentIds);

        return NoContent();
    }

    private static int Clamp(int value) => Math.Clamp(value, 1, 10);
}
