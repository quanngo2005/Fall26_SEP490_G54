namespace G54.BLL.Dtos.Department;

public sealed record DepartmentPerformanceDto(
    decimal? Average,
    decimal? Target,
    decimal? ChangeFromPrevious,
    int EvaluatedEmployeeCount,
    IReadOnlyList<DepartmentKpiPerformanceDto> Kpis);
