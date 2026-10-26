using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiDefinitionSaveDto
{
    [Required]
    public Guid KpiGroupId { get; init; }

    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(255)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [StringLength(1000)]
    public string? Formula { get; init; }

    [Required, StringLength(50)]
    public string Unit { get; init; } = string.Empty;

    /// <summary>UPPER_SNAKE: SUM, RATIO, AVG, NO_AGGREGATION.</summary>
    [Required]
    public string AggregationType { get; init; } = string.Empty;

    /// <summary>UPPER_SNAKE: HIGHER_BETTER, LOWER_BETTER, ON_TARGET.</summary>
    [Required]
    public string Direction { get; init; } = string.Empty;

    [Range(typeof(decimal), "0.0001", "9999.9999")]
    public decimal? MaxAchievementPercent { get; init; }
}
