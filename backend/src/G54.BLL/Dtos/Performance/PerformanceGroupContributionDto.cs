namespace G54.BLL.Dtos.Performance;

/// <summary>A KPI group of the evaluation profile with its weight and the KPI contributions beneath it.</summary>
public sealed record PerformanceGroupContributionDto(
    Guid ContributionId,
    Guid EvaluationProfileItemId,
    Guid KpiGroupId,
    string KpiGroupCode,
    string KpiGroupName,
    decimal WeightPercent,
    decimal? ComponentScore,
    IReadOnlyList<PerformanceKpiContributionDto> Kpis);
