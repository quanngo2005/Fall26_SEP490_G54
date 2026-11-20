namespace G54.BLL.Dtos.Reconciliation;

/// <summary>Query of <c>GET /reconciliations</c> and <c>GET /reconciliations/export</c>.</summary>
public sealed record ReconciliationFilterDto(
    Guid? PeriodId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? DepartmentId = null,
    Guid? JobPositionId = null,
    string? ProductCode = null,
    string? Status = null,
    string? Reason = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>One transaction code of a work and how it compares with the imported Core Banking file.</summary>
public sealed record ReconciliationItemDto(
    Guid Id,
    string TransactionCode,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? JobTitle,
    Guid DepartmentId,
    string? DepartmentName,
    string? CustomerCif,
    string? ReferEmployeeCode,
    string? ProductCode,
    decimal? Amount,
    DateOnly? TransactionDate,
    string Status,
    string? Reason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

public sealed record ReconciliationCountsDto(int All, int NotMatched, int InReview, int Matched);

public sealed record ReconciliationDepartmentOptionDto(Guid Id, string Name);

public sealed record ReconciliationJobPositionOptionDto(Guid Id, string Title);

public sealed record ReconciliationProductOptionDto(string Code, string? Name);

public sealed record ReconciliationFilterOptionsDto(
    IReadOnlyList<ReconciliationDepartmentOptionDto> Departments,
    IReadOnlyList<ReconciliationJobPositionOptionDto> JobPositions,
    IReadOnlyList<ReconciliationProductOptionDto> Products,
    IReadOnlyList<string> Reasons);

/// <summary><c>Counts</c> follow every filter except <c>status</c>, so the status cards keep the whole picture while one is selected.</summary>
public sealed record ReconciliationListDto(
    IReadOnlyList<ReconciliationItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    ReconciliationCountsDto Counts,
    ReconciliationFilterOptionsDto Options);

public sealed record ReconciliationWorkDto(
    Guid Id,
    string Code,
    string Title,
    string Status,
    string? WorkTypeName,
    string? ProductCode,
    string? CustomerName,
    string? CustomerCif);

/// <summary>The latest result submission of the work (null when nothing was submitted yet).</summary>
public sealed record ReconciliationSubmissionDto(
    Guid Id,
    int SubmissionNo,
    string Status,
    DateTimeOffset SubmittedAt,
    string SubmitterName);

/// <summary>The imported row the code was compared with (null until a file holding the code is imported).</summary>
public sealed record ReconciliationImportDto(
    Guid BatchId,
    string FileName,
    DateTimeOffset ImportedAt,
    string? ImportedByName,
    DateOnly TransactionDate,
    decimal Amount);

/// <summary>
/// One compared field. <c>Matched</c> is null when there is nothing to compare (no import yet, a value the employee never
/// declares such as the amount, or a check that does not apply).
/// </summary>
public sealed record ReconciliationFieldDto(string Field, string? Declared, string? Imported, bool? Matched);

public sealed record ReconciliationSiblingDto(Guid Id, string TransactionCode, string Status, string? Reason);

/// <summary><c>Kind</c>: CODE_ADDED, IMPORTED, RECONCILED, QUALITY_FLAG, RISK_EVENT, APPEAL.</summary>
public sealed record ReconciliationHistoryDto(
    string Kind,
    DateTimeOffset At,
    string? Status,
    string? Reason,
    string? Actor,
    string? Reference);

/// <summary>
/// <c>State</c>: NOT_APPLICABLE, AVAILABLE (submission rejected, still inside the window), EXPIRED, IN_PROGRESS, DECIDED.
/// </summary>
public sealed record ReconciliationAppealDto(
    string State,
    DateTimeOffset? Deadline,
    Guid? AppealId,
    string? Status,
    Guid? SubmissionId);

/// <summary>
/// What the caller may do from the detail. <c>FlagBlockedReason</c> explains a disabled flag: NO_PERMISSION,
/// NO_APPROVED_SUBMISSION, OWN_WORK or OPEN_FLAG.
/// </summary>
public sealed record ReconciliationActionsDto(
    bool CanFlag,
    Guid? FlagSubmissionId,
    string? FlagBlockedReason,
    bool CanRecordRisk);

public sealed record ReconciliationDetailDto(
    ReconciliationItemDto Item,
    ReconciliationWorkDto Work,
    ReconciliationSubmissionDto? Submission,
    ReconciliationImportDto? Import,
    IReadOnlyList<ReconciliationFieldDto> Fields,
    int ComparedFields,
    int MatchedFields,
    IReadOnlyList<ReconciliationSiblingDto> Siblings,
    IReadOnlyList<ReconciliationHistoryDto> History,
    ReconciliationAppealDto Appeal,
    ReconciliationActionsDto Actions);

public sealed record ReconciliationExportDto(byte[] Content, string FileName);
