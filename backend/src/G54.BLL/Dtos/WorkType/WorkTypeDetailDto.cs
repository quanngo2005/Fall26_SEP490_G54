namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid WorkflowDefinitionId,
    string WorkflowName,
    WorkTypeStatus Status,
    DateTimeOffset CreatedAt,
    List<WorkTypeKpiDetailDto> Kpis);
