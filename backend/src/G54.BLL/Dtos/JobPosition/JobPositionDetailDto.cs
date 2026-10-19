namespace G54.BLL.Dtos.JobPosition;

public sealed record JobPositionDetailDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    Guid EvaluationProfileId,
    string? EvaluationProfileCode,
    string? EvaluationProfileName,
    string Status,
    int EmployeeCount,
    DateTimeOffset LastSyncAt,
    DateTimeOffset UpdatedAt);
