namespace G54.BLL.Dtos.EvaluationPeriod;

public sealed record CreateEvaluationPeriodRequestDto(
    string? Code,
    string? Name,
    string? PeriodType,
    Guid? ParentPeriodId,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record EvaluationPeriodStatusDto(
    Guid Id,
    string Code,
    string Name,
    string PeriodType,
    Guid? ParentPeriodId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public sealed record EvaluationPeriodFilterDto
{
    /// <summary>Matches period code or name.</summary>
    public string? Keyword { get; init; }

    /// <summary>UPPER_SNAKE period type filter (YEAR, HALF_YEAR, QUARTER, MONTH, WEEK, DAY); invalid value is 400.</summary>
    public string? PeriodType { get; init; }

    /// <summary>UPPER_SNAKE status filter (OPEN, IN_REVIEW, LOCKED, CLOSED); invalid value is 400.</summary>
    public string? Status { get; init; }

    /// <summary>Filters direct child periods of a specified parent period.</summary>
    public Guid? ParentPeriodId { get; init; }

    /// <summary>Filters periods spanning within or touching a given calendar year.</summary>
    public int? Year { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record EvaluationPeriodItemDto(
    Guid Id,
    string Code,
    string Name,
    string PeriodType,
    Guid? ParentPeriodId,
    string? ParentPeriodCode,
    string? ParentPeriodName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTimeOffset UpdatedAt);

/// <summary>Pre-close check of a period: what is still pending when it moves to <c>IN_REVIEW</c>.</summary>
public sealed record ClosePeriodResultDto(
    Guid PeriodId,
    string Status,
    int PendingInReviewResults,
    int OpenDisputes);
