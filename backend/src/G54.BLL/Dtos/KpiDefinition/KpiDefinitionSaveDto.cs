using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiDefinitionSaveDto
{
    [Required]
    public Guid KpiGroupId { get; init; }

    /// <summary>Leave empty on create: the code is generated from <see cref="Name"/>. Required on update (it never changes).</summary>
    [StringLength(50)]
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

    /// <summary>UPPER_SNAKE: PROCESSOR (default), REFERRER.</summary>
    public string? CreditRole { get; init; }

    /// <summary>UPPER_SNAKE: TRANSACTION (default), SERVICE_RATING.</summary>
    public string? DataSource { get; init; }

    /// <summary>The product the KPI belongs to; null when the KPI is not tied to a product.</summary>
    [StringLength(100)]
    public string? ProductName { get; init; }

    [Range(typeof(decimal), "0", "99999999999999.9999")]
    public decimal? BaselineValue { get; init; }

    /// <summary>The job positions the KPI applies to; each must exist and be active.</summary>
    public IReadOnlyList<Guid>? JobPositionIds { get; init; }

    /// <summary>First period the KPI applies to; must be a quarter that is not locked.</summary>
    public Guid? EffectiveFromPeriodId { get; init; }

    /// <summary>Last period the KPI applies to (empty = no end); must not be before <see cref="EffectiveFromPeriodId"/>.</summary>
    public Guid? EffectiveToPeriodId { get; init; }
}
