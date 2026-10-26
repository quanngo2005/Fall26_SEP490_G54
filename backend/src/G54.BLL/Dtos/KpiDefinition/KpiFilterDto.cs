namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiFilterDto
{
    public string? Keyword { get; init; }

    /// <summary>UPPER_SNAKE status filter (DRAFT, ACTIVE, INACTIVE, ARCHIVED); unknown value is 400.</summary>
    public string? Status { get; init; }

    public Guid? KpiGroupId { get; init; }

    /// <summary>UPPER_SNAKE aggregation filter (SUM, RATIO, AVG, NO_AGGREGATION); unknown value is 400.</summary>
    public string? AggregationType { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
