namespace G54.BLL.Dtos.SystemParameter;

/// <param name="Baseline"><c>PREVIOUS_PERIOD</c>, <c>SAME_PERIOD_LAST_YEAR</c> or <c>AVERAGE_LAST_N</c>.</param>
/// <param name="LookbackPeriods">Periods averaged by <c>AVERAGE_LAST_N</c> (2-12); kept for the other baselines.</param>
public sealed record EvaluationComparisonDto(string Baseline, int LookbackPeriods);

public sealed record TierDto(string Code, decimal MinScore);

/// <param name="Required">Tier code to whether the criterion must be met for that tier; a missing tier is not required.</param>
public sealed record TierCriterionDto(
    string Code,
    string Name,
    string Metric,
    decimal Threshold,
    IReadOnlyDictionary<string, bool> Required);

/// <param name="Tiers">The tiers best first with their minimum score, read from <c>ranking_tier_threshold</c>.</param>
/// <param name="Metrics">The metrics a criterion can use.</param>
public sealed record TierCriteriaMatrixDto(
    IReadOnlyList<TierDto> Tiers,
    IReadOnlyList<string> Metrics,
    IReadOnlyList<TierCriterionDto> Criteria);

/// <param name="Criteria">The complete list in display order; a stored criterion that is missing from it is removed.</param>
public sealed record UpdateTierCriteriaDto(IReadOnlyList<TierCriterionDto> Criteria);
