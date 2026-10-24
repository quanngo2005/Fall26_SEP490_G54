using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.EvaluationProfile;

public sealed record EvaluationProfileItemInputDto
{
    [Required]
    public Guid KpiGroupId { get; init; }

    /// <summary>Greater than 0 and at most 100, up to 4 decimal places.</summary>
    public decimal WeightPercent { get; init; }

    /// <summary>1 or greater.</summary>
    public int DisplayOrder { get; init; }
}
