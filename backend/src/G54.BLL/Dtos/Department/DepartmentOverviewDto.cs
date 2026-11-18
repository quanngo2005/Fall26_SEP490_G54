namespace G54.BLL.Dtos.Department;

public sealed record DepartmentOverviewDto(
    DepartmentDetailDto Department,
    EvaluationPeriodDto? Period,
    EvaluationPeriodDto? ComparePeriod,
    IReadOnlyList<EvaluationPeriodDto> AvailablePeriods,
    DepartmentMetricsDto Metrics,
    DepartmentPerformanceDto Performance,
    IReadOnlyList<DepartmentWorkMixDto> WorkMix,
    DepartmentOperationalDto Operational,
    IReadOnlyList<DepartmentAttentionMemberDto> AttentionMembers,
    IReadOnlyList<DepartmentChildUnitDto> ChildUnits,
    DateTimeOffset GeneratedAt);
