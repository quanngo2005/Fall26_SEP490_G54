namespace G54.BLL.Dtos.Performance;

/// <summary>The target and actual snapshot of one KPI, with the scores derived from it.</summary>
public sealed record PerformanceKpiContributionDto(
    Guid ContributionId,
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string Unit,
    Guid TargetAssignmentId,
    decimal TargetValue,
    int TargetVersion,
    decimal ActualValue,
    decimal? ActualDenominator,
    decimal? AchievementPercentRaw,
    decimal? AchievementPercentCapped,
    decimal? ComponentScore);
