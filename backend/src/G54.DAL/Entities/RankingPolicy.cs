namespace G54.DAL.Entities;

/// <summary>
/// Dynamic ranking suggestion policy (GB-910): stores the validated JSON AST definition (score formula, tier thresholds,
/// visual IF-AND-OR automation rules, quota enforcement, and tie-breaker priorities) globally (<c>EvaluationPeriodId == null</c>)
/// or for a specific evaluation period.
/// </summary>
public sealed class RankingPolicy : BaseEntity
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public Guid? EvaluationPeriodId { get; set; }

    public string PolicyJson { get; set; } = "{}";

    public EvaluationPeriod? EvaluationPeriod { get; set; }
}
