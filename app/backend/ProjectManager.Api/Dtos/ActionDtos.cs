namespace ProjectManager.Api.Dtos;

public record ActionDto(
    int Id,
    int ProjectId,
    string Description,
    string Status,
    int Order,
    DateTime? AvailableFrom,
    bool IsEligibleNow,
    DateTime CreatedDate,
    DateTime? CompletedDate);

public record CreateActionRequest(string Description, int? Order, DateTime? AvailableFrom = null);

public record UpdateActionRequest(string? Description, string? Status, int? Order, DateTime? AvailableFrom = null, bool ClearAvailableFrom = false);
