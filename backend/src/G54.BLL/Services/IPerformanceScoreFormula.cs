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

    /// <summary>Scores one KPI group from the scored KPIs beneath it.</summary>
    decimal? CalculateGroup(GroupScoreInput input);

    /// <summary>Scores the whole evaluation from its weighted groups.</summary>
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

public sealed record GroupScoreInput(
    Guid KpiGroupId,
    string KpiGroupCode,
    decimal WeightPercent,
    IReadOnlyList<KpiScoreEntry> Kpis);

public sealed record GroupScoreEntry(Guid KpiGroupId, decimal WeightPercent, decimal? ComponentScore);
