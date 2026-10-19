using G54.DAL.Entities;

namespace G54.BLL.Dtos.JobPosition;

public sealed record JobPositionFilterDto
{
    public string? Keyword { get; init; }
    public RecordStatus? Status { get; init; }
    public Guid? EvaluationProfileId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
