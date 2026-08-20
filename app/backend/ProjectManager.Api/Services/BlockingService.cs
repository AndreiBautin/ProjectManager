using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Models;

namespace ProjectManager.Api.Services;

// Needs AppDbContext, unlike the static PriorityEngine - handles validating and
// persisting project-to-project blocking links, and recomputing dependents'
// derived status when a blocker's own status changes.
public class BlockingService
{
    private readonly AppDbContext _db;
    public BlockingService(AppDbContext db) => _db = db;

    /// <summary>
    /// Returns an error message if the requested blocker set is invalid
    /// (self-reference, unknown project, or would create a circular
    /// dependency), or null if it's fine to apply.
    /// </summary>
    public async Task<string?> ValidateBlockersAsync(int projectId, IEnumerable<int> blockerIds)
    {
        var ids = blockerIds.Distinct().ToList();
        if (ids.Count == 0) return null;

        if (ids.Contains(projectId))
            return "A project cannot block itself.";

        var existingIds = (await _db.Projects
            .Where(p => ids.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();
        var missing = ids.Where(id => !existingIds.Contains(id)).ToList();
        if (missing.Count > 0)
            return $"Unknown project id(s): {string.Join(", ", missing)}.";

        foreach (var blockerId in ids)
        {
            // If blockerId already (transitively) depends on projectId, adding
            // "projectId depends on blockerId" would close a cycle.
            if (await DependsOnAsync(blockerId, projectId))
                return "That would create a circular dependency between projects.";
        }

        return null;
    }

    private async Task<bool> DependsOnAsync(int fromProjectId, int targetProjectId)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(fromProjectId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == targetProjectId) return true;

            var blockingIds = await _db.ProjectBlockers
                .Where(b => b.ProjectId == current)
                .Select(b => b.BlockingProjectId)
                .ToListAsync();

            foreach (var id in blockingIds) queue.Enqueue(id);
        }

        return false;
    }

    /// <summary>
    /// Reconciles project.Blockers (already loaded/tracked) to match blockerIds.
    /// Caller is responsible for calling ValidateBlockersAsync first.
    /// </summary>
    public void SyncBlockers(Project project, List<int> blockerIds)
    {
        var desired = blockerIds.Distinct().ToHashSet();

        foreach (var existing in project.Blockers.ToList())
        {
            if (!desired.Contains(existing.BlockingProjectId))
            {
                project.Blockers.Remove(existing);
                _db.ProjectBlockers.Remove(existing);
            }
        }

        var currentBlockingIds = project.Blockers.Select(b => b.BlockingProjectId).ToHashSet();
        foreach (var id in desired)
        {
            if (!currentBlockingIds.Contains(id))
            {
                project.Blockers.Add(new ProjectBlocker { BlockingProjectId = id });
            }
        }
    }

    /// <summary>
    /// Ids of projects currently waiting on the given project (i.e. it appears
    /// in their Blockers list). Callers that are about to delete a project
    /// should capture this beforehand, since the join rows disappear on cascade
    /// delete.
    /// </summary>
    public async Task<List<int>> GetDependentIdsAsync(int projectId)
    {
        return await _db.ProjectBlockers
            .Where(b => b.BlockingProjectId == projectId)
            .Select(b => b.ProjectId)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// After a project's Status changes (most importantly, to/from Completed),
    /// re-derives Blocked/Active for every project waiting on it, so a project
    /// automatically un-blocks once all its blockers are done - no manual
    /// "unblock" step required.
    /// </summary>
    public async Task RecomputeDependentsAsync(int projectId)
    {
        var dependentIds = await GetDependentIdsAsync(projectId);
        await RecomputeAsync(dependentIds);
    }

    /// <summary>
    /// Re-derives Blocked/Active for a specific set of projects. Used after
    /// deleting a project, since by then the join rows that RecomputeDependentsAsync
    /// would query for are already gone via cascade delete.
    /// </summary>
    public async Task RecomputeAsync(IEnumerable<int> projectIds)
    {
        var ids = projectIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var projects = await _db.Projects
            .Include(p => p.Blockers).ThenInclude(b => b.BlockingProject)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        foreach (var project in projects)
        {
            if (project.Status == ProjectStatus.Completed || project.Status == ProjectStatus.Paused)
                continue;

            var derived = PriorityEngine.DeriveStatus(project, project.Status);
            if (derived != project.Status)
            {
                project.Status = derived;
                project.UpdatedDate = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
    }
}
