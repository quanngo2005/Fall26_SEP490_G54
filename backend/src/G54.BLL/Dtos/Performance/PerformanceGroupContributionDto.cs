namespace G54.BLL.Dtos.Performance;

/// <summary>
/// A KPI group of the evaluation profile with its weight and the KPI contributions beneath it.
/// The group score is computed on each calculation and is not stored.
/// </summary>
public sealed record PerformanceGroupContributionDto(
    Guid EvaluationProfileItemId,
    Guid KpiGroupId,
    string KpiGroupCode,
    string KpiGroupName,
    decimal WeightPercent,
    decimal? ComponentScore,
    IReadOnlyList<PerformanceKpiContributionDto> Kpis);
