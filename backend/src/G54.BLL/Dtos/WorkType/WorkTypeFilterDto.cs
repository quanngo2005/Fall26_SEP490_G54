namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeFilterDto
{
    public string? Keyword { get; init; }
    public WorkTypeStatus? Status { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
