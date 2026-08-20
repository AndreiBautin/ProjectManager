namespace ProjectManager.Api.Dtos;

public record BlockerRef(int Id, string Name, string Status, bool IsResolved);

public record ProjectDto(
    int Id,
    string Name,
    string? Description,
    int? CategoryId,
    string? CategoryName,
    int Impact,
    int Urgency,
    int Effort,
    int PriorityScore,
    string Status,
    int Progress,
    bool IsBlocked,
    string? BlockReason,
    bool IsBlockedByProjects,
    List<BlockerRef> Blockers,
    DateTime? Deadline,
    DateTime CreatedDate,
    DateTime UpdatedDate,
    DateTime? CompletedDate,
    ActionDto? CurrentNextAction,
    List<ActionDto> Actions);

public record CreateProjectRequest(
    string Name,
    string? Description,
    int? CategoryId,
    string? NewCategoryName,
    int Impact = 5,
    int Urgency = 5,
    int Effort = 5,
    bool IsBlocked = false,
    string? BlockReason = null,
    List<int>? BlockedByProjectIds = null,
    string? FirstActionDescription = null,
    DateTime? Deadline = null);

public record UpdateProjectRequest(
    string Name,
    string? Description,
    int? CategoryId,
    int Impact,
    int Urgency,
    int Effort,
    string Status,
    bool IsBlocked,
    string? BlockReason,
    List<int>? BlockedByProjectIds,
    DateTime? Deadline);
