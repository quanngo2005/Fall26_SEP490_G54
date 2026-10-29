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

/// <summary>Pre-close check of a period: what is still pending when it moves to <c>IN_REVIEW</c>.</summary>
public sealed record ClosePeriodResultDto(
    Guid PeriodId,
    string Status,
    int PendingInReviewResults,
    int OpenDisputes);
