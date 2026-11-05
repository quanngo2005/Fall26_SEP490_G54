namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiFilterDto
{
    public string? Keyword { get; init; }

    /// <summary>UPPER_SNAKE status filter (DRAFT, PENDING_APPROVAL, REJECTED, ACTIVE, INACTIVE, ARCHIVED); unknown value is 400.</summary>
    public string? Status { get; init; }

    public Guid? KpiGroupId { get; init; }

    /// <summary>UPPER_SNAKE aggregation filter (SUM, RATIO, AVG, NO_AGGREGATION); unknown value is 400.</summary>
    public string? AggregationType { get; init; }

    /// <summary>Only KPIs that apply to this job position.</summary>
    public Guid? JobPositionId { get; init; }

    /// <summary>UPPER_SNAKE credit role filter (PROCESSOR, REFERRER); unknown value is 400.</summary>
    public string? CreditRole { get; init; }

    /// <summary>UPPER_SNAKE data source filter (TRANSACTION, SERVICE_RATING); unknown value is 400.</summary>
    public string? DataSource { get; init; }

    /// <summary>Only KPIs in force in this period: it lies between the KPI's first and last period (an open end or start counts as in force).</summary>
    public Guid? EffectiveAtPeriodId { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
