using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// The scoring rules of a performance evaluation. Implementations return null for a score they cannot
/// determine; the service stores null and never substitutes a default value.
/// </summary>
public interface IPerformanceScoreFormula
{
    /// <summary>Scores one KPI from its target and aggregated actual.</summary>
    KpiScoreResult CalculateKpi(KpiScoreInput input);

    /// <summary>
    /// Scores one group node of the profile tree from the scored parts beneath it: the weighted mean of its
    /// scored KPI leaves and sub-groups, normalised over the parts that have a score.
    /// </summary>
    decimal? CalculateGroup(GroupScoreInput input);

    /// <summary>Scores the whole evaluation from its weighted root groups.</summary>
    decimal? CalculateTotal(IReadOnlyList<GroupScoreEntry> groups);
}

public sealed record KpiScoreInput(
    Guid KpiDefinitionId,
    string KpiCode,
    string Unit,
    KpiDirection Direction,
    KpiAggregationType AggregationType,
    decimal? MaxAchievementPercent,
    decimal TargetValue,
    decimal ActualValue,
    decimal? ActualDenominator);

public sealed record KpiScoreResult(
    decimal? AchievementPercentRaw,
    decimal? AchievementPercentCapped,
    decimal? ComponentScore);

public sealed record KpiScoreEntry(Guid KpiDefinitionId, string KpiCode, KpiScoreResult Score);

/// <summary>One scored part of a group: a KPI leaf or a sub-group, with its weight among its siblings.</summary>
public sealed record GroupScorePart(decimal WeightPercent, decimal? Score);

/// <summary>
/// A group node of the profile tree (<paramref name="EvaluationProfileItemId"/>) and the parts scored beneath it.
/// </summary>
public sealed record GroupScoreInput(
    Guid EvaluationProfileItemId,
    string Name,
    decimal WeightPercent,
    IReadOnlyList<GroupScorePart> Parts);

public sealed record GroupScoreEntry(Guid EvaluationProfileItemId, decimal WeightPercent, decimal? ComponentScore);
