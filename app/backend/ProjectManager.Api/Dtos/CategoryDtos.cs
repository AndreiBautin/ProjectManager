namespace ProjectManager.Api.Dtos;

public record CategoryDto(int Id, string Name);

public record CreateCategoryRequest(string Name);
