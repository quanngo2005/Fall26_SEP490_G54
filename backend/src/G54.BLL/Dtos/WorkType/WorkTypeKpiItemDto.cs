using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeKpiItemDto
{
    [Required]
    public Guid KpiDefinitionId { get; init; }

    public bool IsRequired { get; init; }

    [Range(typeof(decimal), "0.0001", "10")]
    public decimal Multiplier { get; init; } = 1m;

    [Required]
    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }
}
