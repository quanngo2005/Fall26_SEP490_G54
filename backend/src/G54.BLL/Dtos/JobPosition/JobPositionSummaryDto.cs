namespace G54.BLL.Dtos.JobPosition;

public sealed record JobPositionSummaryDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    int EmployeeCount,
    Guid? EvaluationProfileId,
    string? EvaluationProfileName,
    int WorkTypeCount,
    IReadOnlyList<string> KpiItems,
    DateTimeOffset LastSyncAt,
    string Status);

