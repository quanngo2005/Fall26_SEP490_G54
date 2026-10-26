using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Work;

public sealed record WorkStatusDto
{
    /// <summary>UPPER_SNAKE (PascalCase also accepted): IN_PROGRESS, DONE, CANCELLED.</summary>
    [Required]
    public string Status { get; init; } = string.Empty;

    /// <summary>Required when cancelling; at most 500 characters.</summary>
    [StringLength(500)]
    public string? Reason { get; init; }
}
