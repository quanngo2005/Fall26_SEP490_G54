using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeKpiItemDto
{
    [Required]
    public Guid KpiDefinitionId { get; init; }

    public bool IsRequired { get; init; }

    [Required]
    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }
}
