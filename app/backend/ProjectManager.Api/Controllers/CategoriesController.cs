using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Data;
using ProjectManager.Api.Dtos;
using ProjectManager.Api.Models;

namespace ProjectManager.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CategoriesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> GetAll()
    {
        var categories = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name))
            .ToListAsync();
        return Ok(categories);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Category name is required.");

        var existing = await _db.Categories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
        if (existing != null)
            return Ok(new CategoryDto(existing.Id, existing.Name));

        var category = new Category { Name = name };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return Ok(new CategoryDto(category.Id, category.Name));
    }
}
