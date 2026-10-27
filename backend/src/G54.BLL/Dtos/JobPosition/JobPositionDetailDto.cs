namespace G54.BLL.Dtos.JobPosition;

public sealed record JobPositionEvaluationProfileItemDto(
    Guid Id,
    string GroupName,
    string GroupCode,
    decimal WeightPercent,
    int DisplayOrder);

public sealed record JobPositionEvaluationProfileDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int Version,
    DateOnly EffectiveFrom,
    string Status,
    decimal TotalWeightPercent,
    IReadOnlyList<JobPositionEvaluationProfileItemDto> Items);

public sealed record JobPositionKpiDto(
    Guid Id,
    string Code,
    string Name,
    bool IsRequired,
    string Unit);

public sealed record JobPositionWorkTypeDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int KpiCount,
    IReadOnlyList<JobPositionKpiDto> Kpis);

public sealed record JobPositionDetailDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    int EmployeeCount,
    DateTimeOffset LastSyncAt,
    string Status,
    JobPositionEvaluationProfileDto? EvaluationProfile,
    IReadOnlyList<JobPositionWorkTypeDto> WorkTypes);

