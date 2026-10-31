namespace G54.BLL.Dtos.Performance;

/// <summary>
/// A root group of the evaluation profile tree with its weight and the KPI contributions beneath it (leaves of
/// sub-groups are listed flat). The group score is computed on each calculation and is not stored.
/// </summary>
public sealed record PerformanceGroupContributionDto(
    Guid EvaluationProfileItemId,
    string GroupName,
    decimal WeightPercent,
    decimal? ComponentScore,
    IReadOnlyList<PerformanceKpiContributionDto> Kpis);
