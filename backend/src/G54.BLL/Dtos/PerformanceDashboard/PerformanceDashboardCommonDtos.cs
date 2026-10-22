namespace G54.BLL.Dtos.PerformanceDashboard;

/// <summary>An evaluation period shown by the dashboard.</summary>
public sealed record DashboardPeriodDto(
    Guid Id,
    string Code,
    string Name,
    string PeriodType,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>An organisation node (branch, department or team).</summary>
public sealed record DashboardOrgUnitDto(
    Guid Id,
    string Code,
    string Name,
    string LevelType);
