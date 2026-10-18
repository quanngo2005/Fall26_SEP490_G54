namespace G54.BLL.Dtos.Department;

public sealed record DepartmentOverviewDto(
    DepartmentDetailDto Department,
    EvaluationPeriodDto? Period,
    IReadOnlyList<EvaluationPeriodDto> AvailablePeriods,
    DepartmentMetricsDto Metrics,
    DepartmentPerformanceDto Performance,
    IReadOnlyList<DepartmentWorkMixDto> WorkMix,
    DepartmentOperationalDto Operational,
    DateTimeOffset GeneratedAt);
