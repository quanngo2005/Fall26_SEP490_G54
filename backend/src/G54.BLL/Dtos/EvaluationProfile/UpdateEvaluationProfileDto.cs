using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>Replaces the profile; `Items` replaces the whole item list. Code and version never change.</summary>
public sealed record UpdateEvaluationProfileDto
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Required]
    public Guid JobPositionId { get; init; }

    [Required]
    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    public List<EvaluationProfileItemInputDto> Items { get; init; } = [];
}
