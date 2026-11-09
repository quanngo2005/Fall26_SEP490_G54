namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiJobPositionDto(Guid Id, string Code, string Title);

/// <summary>The code a new KPI with that name would get (<c>GET /kpi-definitions/code-suggestion</c>).</summary>
public sealed record KpiCodeSuggestionDto(string Code);

/// <summary>An evaluation period a KPI starts or stops applying in.</summary>
public sealed record KpiPeriodRefDto(Guid Id, string Code, string Name, DateOnly StartDate, DateOnly EndDate);

/// <summary>How many KPIs are in each status, for the tabs of the KPI list.</summary>
public sealed record KpiSummaryDto(int Total, int Draft, int PendingApproval, int Rejected, int Active, int Inactive, int Archived);

public sealed record KpiUsageObjectiveDto(Guid Id, string Code);

/// <summary>A KPI of the same product, shown beside the KPI as the other credit role of the product.</summary>
public sealed record KpiRelatedItemDto(
    Guid Id,
    string Code,
    string Name,
    string CreditRole,
    string DataSource,
    IReadOnlyList<string> JobPositions);

/// <summary>Where a KPI is used: the business objectives it is linked to, its active targets, and the other KPIs of its product.</summary>
public sealed record KpiUsageDto(
    IReadOnlyList<KpiUsageObjectiveDto> Objectives,
    int ActiveTargetCount,
    IReadOnlyList<KpiRelatedItemDto> RelatedItems);

/// <summary>One change of a KPI taken from the audit log; <c>Action</c> is CREATE, UPDATE, SUBMIT, APPROVE, REJECT, DEACTIVATE, REACTIVATE or ARCHIVE.</summary>
public sealed record KpiHistoryEntryDto(DateTimeOffset OccurredAt, string? ActorName, string Action, string Summary);
