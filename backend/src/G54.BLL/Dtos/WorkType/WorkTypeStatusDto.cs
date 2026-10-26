using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeStatusDto
{
    /// <summary>UPPER_SNAKE: ACTIVE, INACTIVE, ARCHIVED.</summary>
    [Required]
    public string Status { get; init; } = string.Empty;
}
