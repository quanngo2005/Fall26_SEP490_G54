using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.EvaluationProfile;

public sealed record CreateEvaluationProfileDto
{
    /// <summary>An existing code creates the next version of that profile.</summary>
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

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
