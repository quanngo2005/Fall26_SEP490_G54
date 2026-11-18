namespace G54.BLL.Dtos.Verification;

/// <summary>Query of <c>GET /verifications</c>. Every filter is optional.</summary>
public sealed class VerificationQueueFilterDto
{
    /// <summary><c>SUBMITTED</c> or <c>UNDER_REVIEW</c> (any casing). Empty means both.</summary>
    public string? Status { get; set; }

    /// <summary><c>NONE</c>, <c>MATCHED</c>, <c>NOT_MATCHED</c> or <c>IN_REVIEW</c> (any casing). Empty means every match status.</summary>
    public string? MatchStatus { get; set; }

    /// <summary>Submitters of this department and its sub-departments, within the caller's scope.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Case-insensitive contains on the work code or title, or the submitter's code or name.</summary>
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>How the transaction codes attached to the submitted work were reconciled.</summary>
public static class VerificationMatchStatus
{
    /// <summary>The work has no transaction code.</summary>
    public const string None = "NONE";

    /// <summary>Every code is matched.</summary>
    public const string Matched = "MATCHED";

    /// <summary>At least one code is not matched (final).</summary>
    public const string NotMatched = "NOT_MATCHED";

    /// <summary>No code is rejected, but at least one still waits for imported data.</summary>
    public const string InReview = "IN_REVIEW";
}

public sealed record VerificationQueueItemDto(
    Guid SubmissionId,
    string SubmissionCode,
    int SubmissionNo,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    string? WorkTypeName,
    Guid SubmitterEmployeeId,
    string SubmitterEmployeeCode,
    string SubmitterName,
    Guid DepartmentId,
    string DepartmentName,
    DateTimeOffset SubmittedAt,
    string Status,
    string MatchStatus,
    int TransactionCount,
    int MatchedTransactionCount);
