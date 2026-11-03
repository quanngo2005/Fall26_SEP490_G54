using G54.DAL.Entities;

namespace G54.BLL.Dtos.KpiDefinition;

/// <summary>
/// Enum members are UPPER_SNAKE API values: `AggregationType` (<see cref="KpiAggregationType"/>) SUM, RATIO, AVG, NO_AGGREGATION;
/// `Direction` (<see cref="KpiDirection"/>) HIGHER_BETTER, LOWER_BETTER, ON_TARGET;
/// `Status` (<see cref="KpiStatus"/>) DRAFT, PENDING_APPROVAL, REJECTED, ACTIVE, INACTIVE, ARCHIVED.
/// `RejectReason` is set only while the status is REJECTED; `RequestNote` and `RequestedBy` only for a KPI created through a request.
/// </summary>
public sealed record KpiDefinitionDto(
    Guid Id,
    Guid KpiGroupId,
    string KpiGroupName,
    string Code,
    string Name,
    string? Description,
    string? Formula,
    string Unit,
    string AggregationType,
    string Direction,
    decimal? MaxAchievementPercent,
    string Status,
    DateTimeOffset UpdatedAt,
    string? RejectReason = null,
    string? RequestNote = null,
    Guid? RequestedBy = null);
