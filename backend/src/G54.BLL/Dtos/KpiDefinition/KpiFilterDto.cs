using G54.DAL.Entities;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiFilterDto
{
    public string? Keyword { get; init; }
    public KpiStatus? Status { get; init; }
    public Guid? KpiGroupId { get; init; }
    public KpiAggregationType? AggregationType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
