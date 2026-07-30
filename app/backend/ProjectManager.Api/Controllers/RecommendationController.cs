using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Services;

namespace ProjectManager.Api.Controllers;

[ApiController]
[Route("api/recommendation")]
public class RecommendationController : ControllerBase
{
    private readonly AppDbContext _db;
    public RecommendationController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<RecommendationResult>> Get()
    {
        var projects = await _db.Projects
            .Include(p => p.Actions)
            .AsNoTracking()
            .ToListAsync();

        return Ok(PriorityEngine.GetRecommendation(projects));
    }
}
