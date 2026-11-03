using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiStatusDto
{
    /// <summary>UPPER_SNAKE: DRAFT, PENDING_APPROVAL, REJECTED, ACTIVE, INACTIVE, ARCHIVED (see the contract for the allowed transitions).</summary>
    [Required]
    public string Status { get; init; } = string.Empty;
}
