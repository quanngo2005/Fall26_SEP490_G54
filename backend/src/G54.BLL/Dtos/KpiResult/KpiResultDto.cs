namespace G54.BLL.Dtos.KpiResult;

/// <summary>
/// A verified KPI value: one WorkResultItem of a VerifiedWorkResult.
/// Contract only (GB-252); the endpoints are implemented in GB-251.
/// See docs/api/SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md.
/// </summary>
public sealed record KpiResultDto(
    Guid WorkResultItemId,
    Guid VerifiedWorkResultId,
    Guid WorkId,
    string WorkCode,
    Guid SubmissionId,
    Guid EmployeeId,
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string Unit,
    decimal Value,
    decimal? Denominator,
    string Status,
    DateTimeOffset VerifiedAt,
    DateTimeOffset? InvalidatedAt);

/// <summary>Query string for GET /api/v1/kpi-results.</summary>
public sealed record KpiResultQueryDto
{
    public Guid? EmployeeId { get; init; }
    public Guid? KpiDefinitionId { get; init; }
    public Guid? EvaluationPeriodId { get; init; }

    /// <summary>VALID | INVALID; null returns both.</summary>
    public string? Status { get; init; }

    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
