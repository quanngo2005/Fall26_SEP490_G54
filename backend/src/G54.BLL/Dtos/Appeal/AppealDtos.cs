using Microsoft.AspNetCore.Http;

namespace G54.BLL.Dtos.Appeal;

public sealed record ReviewAppealRequestDto(decimal? Points, string? Reason);
public sealed record ScoreDisputeRequestDto(string? Reason);
public sealed record AppealScoreSettingDto(decimal DefaultPoints);
public sealed record ScoreEntryDto(Guid Id, Guid? AppealId, Guid? WorkId, Guid EvaluationId, decimal Points,
    string Kind, string Source, string Reason, Guid RecordedByEmployeeId, DateTimeOffset CreatedAt, Guid? ReversesEntryId);

public sealed record DecideAppealRequestDto
{
    public string? Decision { get; init; }

    public string? Note { get; init; }

    public decimal? VerifiedValue { get; init; }
}

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
    /// <summary>UPPER_SNAKE status filter (OPEN, UNDER_REVIEW, ACCEPTED, REJECTED, WITHDRAWN); unknown value is 400.</summary>
    public string? Status { get; init; }

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
    DateTimeOffset UpdatedAt);

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
    string? ScoreDisputeReason = null);
