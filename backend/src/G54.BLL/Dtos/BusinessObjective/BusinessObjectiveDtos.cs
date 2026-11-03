using G54.DAL.Entities;

namespace G54.BLL.Dtos.BusinessObjective;

/// <summary>
/// `Status` is the UPPER_SNAKE value of <see cref="BusinessObjectiveStatus"/>: DRAFT, ACTIVE, CLOSED, CANCELLED.
/// `PriorityWeight` is null until a Branch Manager sets it.
/// </summary>
public sealed record BusinessObjectiveDto(
    Guid Id,
    Guid? ParentObjectiveId,
    string Code,
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal? PriorityWeight,
    string Status,
    DateTimeOffset UpdatedAt);

/// <summary>
/// How many records reference an objective; the objective can only be deleted while all are 0.
/// `TargetCount` counts only the targets the caller may see.
/// </summary>
public sealed record BusinessObjectiveUsageDto(
    int KpiCount,
    int TargetCount,
    int CampaignCount,
    int ChildObjectiveCount);

public sealed record ObjectiveKpiLinkDto(
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string Unit);

public sealed record BusinessObjectiveDetailDto(
    BusinessObjectiveDto Objective,
    BusinessObjectiveUsageDto Usage,
    IReadOnlyList<ObjectiveKpiLinkDto> Kpis);
