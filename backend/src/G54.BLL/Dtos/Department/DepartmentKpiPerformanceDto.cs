namespace G54.BLL.Dtos.Department;

public sealed record DepartmentKpiPerformanceDto(
    Guid KpiDefinitionId,
    string Code,
    string Name,
    string Unit,
    string Direction,
    decimal TargetValue,
    decimal? ActualValue,
    decimal? AchievementPercent);
