using G54.DAL.Entities;

namespace G54.BLL.Dtos.KpiDefinition;

/// <summary>
/// Enum members are UPPER_SNAKE API values: `AggregationType` (<see cref="KpiAggregationType"/>) SUM, RATIO, AVG, NO_AGGREGATION;
/// `Direction` (<see cref="KpiDirection"/>) HIGHER_BETTER, LOWER_BETTER, ON_TARGET;
/// `Status` (<see cref="KpiStatus"/>) DRAFT, PENDING_APPROVAL, REJECTED, ACTIVE, INACTIVE, ARCHIVED.
/// `RejectReason` is set only while the status is REJECTED; `RequestNote` and `RequestedBy` only for a KPI created through a request.
/// `CreditRole` (<see cref="KpiCreditRole"/>) PROCESSOR, REFERRER; `DataSource` (<see cref="KpiDataSource"/>) TRANSACTION, SERVICE_RATING.
/// `EffectiveFrom` / `EffectiveTo` are the first and last period the KPI applies to (null = always / no end).
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
    Guid? RequestedBy = null,
    string CreditRole = "PROCESSOR",
    string DataSource = "TRANSACTION",
    string? ProductName = null,
    decimal? BaselineValue = null,
    IReadOnlyList<KpiJobPositionDto>? JobPositions = null,
    KpiPeriodRefDto? EffectiveFrom = null,
    KpiPeriodRefDto? EffectiveTo = null,
    DateTimeOffset? SubmittedAt = null);
