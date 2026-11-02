using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.EvaluationProfile;

public sealed record EvaluationProfileStatusDto
{
    /// <summary>UPPER_SNAKE: DRAFT, PENDING_APPROVAL, REJECTED, ACTIVE, INACTIVE, ARCHIVED.</summary>
    [Required]
    public string Status { get; init; } = string.Empty;
}
