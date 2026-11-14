namespace G54.BLL.Dtos.Department;

public sealed record DepartmentPerformanceDto(
    decimal? Average,
    decimal? PreviousAverage,
    decimal? Target,
    decimal? ChangeFromPrevious,
    int EvaluatedEmployeeCount,
    IReadOnlyList<DepartmentKpiPerformanceDto> Kpis,
    IReadOnlyList<DepartmentTrendPointDto> Trend,
    IReadOnlyList<DepartmentKpiGroupDto> KpiGroups,
    IReadOnlyList<DepartmentPositionDto> Positions);
