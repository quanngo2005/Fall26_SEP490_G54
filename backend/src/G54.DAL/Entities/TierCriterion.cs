namespace G54.DAL.Entities;

/// <summary>
/// One administrator-defined criterion of the tier matrix: a metric of an evaluation and the threshold it has to reach.
/// <see cref="TierCriterionRequirement"/> says for which tiers the criterion must be met.
/// </summary>
public sealed class TierCriterion : BaseEntity
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>
    /// <c>TOTAL_SCORE</c>, <c>BUSINESS_SCORE</c>, <c>SERVICE_QUALITY_SCORE</c> and <c>OPERATIONS_SCORE</c> are met at or above
    /// <see cref="Threshold"/>; <c>OPEN_RISK_EVENTS</c> is met at or below it.
    /// </summary>
    public string Metric { get; set; } = null!;

    public decimal Threshold { get; set; }

    public int SortOrder { get; set; }

    public ICollection<TierCriterionRequirement> Requirements { get; set; } = [];
}

/// <summary>A cell of the tier matrix: whether a criterion must be met ("Đạt") for an employee to be proposed a tier.</summary>
public sealed class TierCriterionRequirement : BaseEntity
{
    public Guid TierCriterionId { get; set; }

    public string TierCode { get; set; } = null!;

    public bool Required { get; set; }

    public TierCriterion TierCriterion { get; set; } = null!;
}
