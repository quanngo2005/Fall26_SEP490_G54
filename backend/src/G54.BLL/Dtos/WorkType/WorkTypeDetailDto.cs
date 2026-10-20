using G54.DAL.Entities;

namespace G54.BLL.Dtos.WorkType;

/// <summary>`Status` is the UPPER_SNAKE wire value of <see cref="RecordStatus"/>: ACTIVE, INACTIVE, ARCHIVED.</summary>
public sealed record WorkTypeDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid WorkflowDefinitionId,
    string WorkflowName,
    string Status,
    DateTimeOffset CreatedAt,
    List<WorkTypeKpiDetailDto> Kpis);
