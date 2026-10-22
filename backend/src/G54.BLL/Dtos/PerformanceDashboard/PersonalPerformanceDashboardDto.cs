namespace G54.BLL.Dtos.PerformanceDashboard;

/// <summary>
/// Performance of one employee in one period. See docs/api/PERFORMANCE_DASHBOARD_CONTRACT.md.
/// </summary>
public sealed record PersonalPerformanceDashboardDto(
    DashboardEmployeeDto Employee,
    DashboardPeriodDto? Period,
    IReadOnlyList<DashboardPeriodDto> AvailablePeriods,
    string? EvaluationStatus,
    decimal? TotalScore,
    decimal? PreviousTotalScore,
    decimal? ChangeFromPrevious,
    DateTimeOffset? CalculatedAt,
    IReadOnlyList<DashboardGroupDto> Groups,
    DateTimeOffset GeneratedAt);

public sealed record DashboardEmployeeDto(
    Guid Id,
    string Code,
    string FullName,
    Guid DepartmentId,
    string DepartmentName,
    string JobPositionTitle);

/// <summary>A KPI group of the evaluation profile with its weight and the KPIs scored beneath it.</summary>
public sealed record DashboardGroupDto(
    Guid KpiGroupId,
    string Code,
    string Name,
    decimal WeightPercent,
    decimal? Score,
    IReadOnlyList<DashboardKpiDto> Kpis);

/// <summary>The stored target, actual and scores of one KPI.</summary>
public sealed record DashboardKpiDto(
    Guid KpiDefinitionId,
    string Code,
    string Name,
    string Unit,
    string Direction,
    decimal? TargetValue,
    decimal? ActualValue,
    decimal? AchievementPercentRaw,
    decimal? AchievementPercentCapped,
    decimal? Score);
