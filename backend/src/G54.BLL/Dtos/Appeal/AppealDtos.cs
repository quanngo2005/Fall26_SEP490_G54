using Microsoft.AspNetCore.Http;

namespace G54.BLL.Dtos.Appeal;

/// <summary>Whose appeals <c>GET /appeals</c> lists, as exposed in the API <c>scope</c> query.</summary>
public static class AppealScopes
{
    public const string Mine = "MINE";
    public const string Team = "TEAM";
}

/// <summary>The values of <c>decision</c> accepted by <c>POST /appeals/{id}/decision</c>.</summary>
public static class AppealDecisions
{
    public const string Accept = "ACCEPT";
    public const string Adjust = "ADJUST";
    public const string Reject = "REJECT";
}

public sealed record DecideAppealRequestDto
{
    public string? Decision { get; init; }

    public string? Note { get; init; }

    public decimal? VerifiedValue { get; init; }
}

/// <summary><c>VerificationId</c> is null when the appeal was rejected.</summary>
public sealed record AppealDecisionResultDto(
    AppealDetailDto Appeal,
    string Decision,
    string SubmissionStatus,
    string WorkStatus,
    Guid? VerificationId);

/// <summary>Multipart form of <c>POST /submissions/{submissionId}/appeals</c>.</summary>
public sealed class CreateAppealRequestDto
{
    public string? Reason { get; set; }

    public List<IFormFile>? Files { get; set; }
}

public sealed record AppealFilterDto
{
    /// <summary>
    /// UPPER_SNAKE status filter (OPEN, UNDER_REVIEW, ACCEPTED, REJECTED, WITHDRAWN), one value or a comma separated
    /// list; unknown value is 400.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>Only appeals of results submitted in this evaluation period.</summary>
    public Guid? PeriodId { get; init; }

    /// <summary>Only appeals of employees holding this job position.</summary>
    public Guid? JobPositionId { get; init; }

    /// <summary>
    /// Whose appeals: <c>MINE</c> only the caller's own, <c>TEAM</c> only other employees' within the caller's scope;
    /// empty means both; unknown value is 400.
    /// </summary>
    public string? Scope { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record AppealListItemDto(
    Guid Id,
    Guid ResultSubmissionId,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    int SubmissionNo,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string Status,
    DateTimeOffset FiledAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset UpdatedAt,
    string? JobPositionTitle = null,
    string? ResolvedByEmployeeName = null,
    string? Note = null);

public sealed record AppealEvidenceDto(
    Guid Id,
    string FileName,
    long? FileSize,
    string? MimeType,
    string PreviewUrl,
    string DownloadUrl);

public sealed record AppealDetailDto(
    Guid Id,
    Guid ResultSubmissionId,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    int SubmissionNo,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string Status,
    DateTimeOffset FiledAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset UpdatedAt,
    string Reason,
    string? Note,
    Guid? ResolvedByEmployeeId,
    string? ResolvedByEmployeeName,
    IReadOnlyList<AppealEvidenceDto> Evidences,
    DateTimeOffset SubmittedAt,
    string? WorkTypeName = null,
    string? ReferrerEmployeeCode = null,
    string? ReferrerEmployeeName = null);
