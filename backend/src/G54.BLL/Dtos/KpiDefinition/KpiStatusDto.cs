using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiStatusDto
{
    /// <summary>UPPER_SNAKE: DRAFT, ACTIVE, INACTIVE, ARCHIVED.</summary>
    [Required]
    public string Status { get; init; } = string.Empty;
}
