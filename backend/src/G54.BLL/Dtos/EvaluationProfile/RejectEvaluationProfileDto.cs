using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.EvaluationProfile;

public sealed record RejectEvaluationProfileDto
{
    /// <summary>Why the profile is sent back; required, max 500 characters.</summary>
    [Required]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}
