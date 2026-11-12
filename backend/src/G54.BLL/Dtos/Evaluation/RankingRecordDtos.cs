namespace G54.BLL.Dtos.Evaluation;

/// <summary>Query of <c>GET /ranking-records</c>. Only <see cref="PeriodId"/> is required.</summary>
public sealed class RankingRecordFilterDto
{
    /// <summary>The locked evaluation period whose records are listed.</summary>
    public Guid? PeriodId { get; set; }

    /// <summary>Employees of this department and its sub-departments.</summary>
    public Guid? DepartmentId { get; set; }

    public Guid? JobPositionId { get; set; }

    /// <summary>A configured tier code (any casing).</summary>
    public string? Tier { get; set; }

    /// <summary>Case-insensitive contains on the employee code or full name.</summary>
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>One finalized evaluation of a locked period, ranked inside its department and job position.</summary>
public sealed record RankingRecordDto(
    Guid EvaluationId,
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    Guid DepartmentId,
    string DepartmentName,
    Guid JobPositionId,
    string JobPositionTitle,
    decimal TotalScore,
    int Rank,
    int GroupSize,
    string ProposedTier,
    string Tier,
    bool IsAdjusted,
    DateTimeOffset? LockedAt);

/// <summary>A ranking record with its period and the review information saved with the evaluation.</summary>
public sealed record RankingRecordDetailDto(
    RankingRecordDto Record,
    Guid PeriodId,
    string PeriodCode,
    string PeriodName,
    string? AdjustmentReason,
    string? CoachingPlan,
    string? ApprovalComments,
    DateTimeOffset? AcknowledgedAt);
