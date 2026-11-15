namespace G54.BLL.Dtos.Submission;

/// <summary>Query of <c>GET /submissions/mine</c>. Every filter is optional.</summary>
public sealed class MySubmissionFilterDto
{
    /// <summary><c>NONE</c>, <c>MATCHED</c>, <c>NOT_MATCHED</c> or <c>IN_REVIEW</c> (any casing). Empty means every match status.</summary>
    public string? MatchStatus { get; set; }

    /// <summary>Only submissions sent inside this evaluation period.</summary>
    public Guid? PeriodId { get; set; }

    /// <summary>Case-insensitive contains on the work code, work title or a transaction code of the work.</summary>
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>Where the appeal window of a submission stands (<c>SystemParameters:AppealRejectedHours</c>).</summary>
public static class MySubmissionAppealState
{
    /// <summary>Only a REJECTED submission can be appealed.</summary>
    public const string NotApplicable = "NOT_APPLICABLE";

    /// <summary>Rejected and still inside the appeal window.</summary>
    public const string Available = "AVAILABLE";

    /// <summary>Rejected, but the appeal window has closed.</summary>
    public const string Expired = "EXPIRED";

    /// <summary>An appeal of this submission is awaiting a decision.</summary>
    public const string InProgress = "IN_PROGRESS";
}

/// <summary>Which value of a transaction is compared in <see cref="MySubmissionFieldCheckDto"/>.</summary>
public static class MySubmissionField
{
    public const string TransactionCode = "TRANSACTION_CODE";
    public const string Employee = "EMPLOYEE";
    public const string Product = "PRODUCT";
    public const string Amount = "AMOUNT";
}

/// <summary>One row of the caller's submission history: the latest version of each work.</summary>
public sealed record MySubmissionListItemDto(
    Guid SubmissionId,
    string SubmissionCode,
    int SubmissionNo,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    string? WorkTypeName,
    DateTimeOffset SubmittedAt,
    string Status,
    string MatchStatus,
    int TransactionCount,
    int MatchedTransactionCount,
    string? FirstTransactionCode,
    DateTimeOffset? AppealDeadline);

/// <summary>
/// One compared value. <c>Matched</c> is null when there is nothing to compare: no imported record yet, or the
/// value has no declared counterpart (the amount).
/// </summary>
public sealed record MySubmissionFieldCheckDto(
    string Field,
    string? Declared,
    string? Imported,
    bool? Matched);

public sealed record MySubmissionTransactionDto(
    string TransactionCode,
    string Status,
    string? Reason,
    IReadOnlyList<MySubmissionFieldCheckDto> Fields);

public sealed record MySubmissionVersionDto(
    Guid SubmissionId,
    int SubmissionNo,
    DateTimeOffset SubmittedAt,
    string Status,
    string MatchStatus);

public sealed record MySubmissionAppealDto(
    string State,
    DateTimeOffset? Deadline,
    Guid? AppealId);

/// <summary>A submission of the caller with how its transaction codes were reconciled.</summary>
public sealed record MySubmissionDetailDto(
    Guid SubmissionId,
    string SubmissionCode,
    int SubmissionNo,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    string? WorkTypeName,
    DateTimeOffset SubmittedAt,
    string Status,
    string MatchStatus,
    IReadOnlyList<SubmissionEvidenceDto> Evidences,
    IReadOnlyList<MySubmissionTransactionDto> Transactions,
    IReadOnlyList<MySubmissionVersionDto> Versions,
    MySubmissionAppealDto Appeal);
