namespace G54.BLL.Dtos.PerformanceDashboard;

/// <summary>
/// Performance of the subtree of a department or branch in one period.
/// See docs/api/PERFORMANCE_DASHBOARD_CONTRACT.md.
/// </summary>
public sealed record UnitPerformanceDashboardDto(
    DashboardOrgUnitDto OrgUnit,
    DashboardPeriodDto? Period,
    IReadOnlyList<DashboardPeriodDto> AvailablePeriods,
    int EmployeeCount,
    int EvaluatedEmployeeCount,
    decimal? AverageScore,
    decimal? PreviousAverageScore,
    decimal? ChangeFromPrevious,
    IReadOnlyList<DashboardUnitGroupDto> Groups,
    IReadOnlyList<DashboardUnitKpiDto> Kpis,
    IReadOnlyList<DashboardChildUnitDto> ChildUnits,
    IReadOnlyList<DashboardMemberDto> Members,
    DateTimeOffset GeneratedAt);

public sealed record DashboardUnitGroupDto(
    Guid KpiGroupId,
    string Code,
    string Name,
    decimal? AverageScore,
    int EmployeeCount);

public sealed record DashboardUnitKpiDto(
    Guid KpiDefinitionId,
    string Code,
    string Name,
    string Unit,
    string Direction,
    int EmployeeCount,
    decimal? AverageAchievementPercent,
    decimal? AverageScore);

/// <summary>A direct child of the node; its figures cover the child's whole subtree.</summary>
public sealed record DashboardChildUnitDto(
    Guid Id,
    string Code,
    string Name,
    string LevelType,
    int EmployeeCount,
    int EvaluatedEmployeeCount,
    decimal? AverageScore);

/// <summary>An employee attached directly to the node.</summary>
public sealed record DashboardMemberDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string JobPositionTitle,
    string? EvaluationStatus,
    decimal? TotalScore);
