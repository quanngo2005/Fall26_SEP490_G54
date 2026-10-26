namespace G54.BLL.Dtos.Performance;

/// <summary>
/// Result of calculating a performance evaluation.
/// See docs/api/PERFORMANCE_EVALUATION_CONTRACT.md.
/// </summary>
public sealed record PerformanceEvaluationDto(
    Guid Id,
    Guid EmployeeId,
    Guid EvaluationPeriodId,
    Guid EvaluationProfileId,
    decimal? TotalScore,
    string Status,
    DateTimeOffset? CalculatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PerformanceGroupContributionDto> Groups);
