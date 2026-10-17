using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.WorkType;

public sealed record CreateWorkTypeDto
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Required]
    public Guid WorkflowDefinitionId { get; init; }

    public List<WorkTypeKpiItemDto> Kpis { get; init; } = [];
}
