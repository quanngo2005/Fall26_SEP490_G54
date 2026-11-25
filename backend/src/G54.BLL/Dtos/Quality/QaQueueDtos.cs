namespace G54.BLL.Dtos.Quality;

public sealed class QaQueueFilterDto
{
    /// <summary>PENDING (default), CONFIRMED or REJECTED.</summary>
    public string? Tab { get; set; }

    /// <summary>FLAG or SAMPLE; empty for both.</summary>
    public string? Kind { get; set; }

    /// <summary>The department of the employee, including its sub-departments.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Only items created inside this evaluation period.</summary>
    public Guid? PeriodId { get; set; }

    /// <summary>QA code (<c>QA-0331</c> or <c>331</c>), work code or title, employee code or name.</summary>
    public string? Search { get; set; }

    /// <summary>Column to sort by: code, type, work, employee, source, created, due or status. Default: due date for PENDING, newest first otherwise.</summary>
    public string? SortBy { get; set; }

    /// <summary>asc (default) or desc.</summary>
    public string? SortDir { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>
/// One row of the QA queue: a quality flag, or a 5% sample that has not produced a flag. <c>Kind</c> is the record to act on
/// (FLAG or SAMPLE); <c>Type</c> is what QA sees: a flag raised from a sample stays a SAMPLE, with the sample's code.
/// </summary>
public sealed record QaQueueItemDto(
    string Kind,
    string Type,
    Guid Id,
    string Code,
    string Source,
    Guid ResultSubmissionId,
    int SubmissionNo,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? DepartmentName,
    string Tab,
    string Status,
    string? Severity,
    DateTimeOffset CreatedAt,
    DateTimeOffset DueAt);

public sealed record QaQueueCountsDto(int Pending, int Confirmed, int Rejected);

public sealed record QaDepartmentOptionDto(Guid Id, string Name);

/// <summary><c>Departments</c> lists every department that has a QA item, so the department filter needs no other permission.</summary>
public sealed record QaQueueDto(
    List<QaQueueItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    QaQueueCountsDto Counts,
    List<QaDepartmentOptionDto> Departments);

public sealed record QaEvidenceDto(Guid Id, string FileName, long FileSize, string MimeType);

/// <summary>The imported Core Banking transaction a work was matched against (the reconciliation panel).</summary>
public sealed record QaTransactionDto(
    string TransactionCode,
    string Status,
    string? Reason,
    string? ImportBatchFileName,
    string? ProductCode,
    decimal? Amount,
    DateOnly? TransactionDate);

/// <summary>One result submission of the work (SCR-REV-02), newest first.</summary>
public sealed record QaSubmissionVersionDto(
    Guid Id,
    int SubmissionNo,
    string Status,
    DateTimeOffset SubmittedAt,
    string? Comment,
    List<QaEvidenceDto> Evidences);

/// <summary>One verifier decision on a submission of the work.</summary>
public sealed record QaVerificationDto(
    int SubmissionNo,
    int RoundNo,
    string Decision,
    string? Comment,
    DateTimeOffset? VerifiedAt,
    string VerifierName);

/// <summary>
/// What QA decided on the item. A flag fills every field it has; a sample only has its reviewer and time
/// (its comment is the detail's <c>Reason</c>).
/// </summary>
public sealed record QaReviewDto(
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt,
    string? DismissReason,
    string? ReworkComment,
    DateTimeOffset? ReworkRequestedAt);

public sealed record QaQueueDetailDto(
    QaQueueItemDto Item,
    string? Reason,
    string? SubmissionComment,
    DateTimeOffset SubmittedAt,
    string? JobPositionTitle,
    List<QaEvidenceDto> Evidences,
    List<QaTransactionDto> Transactions,
    List<QaSubmissionVersionDto> Versions,
    List<QaVerificationDto> VerificationHistory,
    QaReviewDto QaReview);
