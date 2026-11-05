namespace G54.BLL.Dtos.Quality;

/// <summary>QA flags an approved result. <c>Severity</c> is LOW, MEDIUM (default) or HIGH.</summary>
public sealed record CreateQualityFlagDto(string? Reason, string? Severity);

/// <summary>QA sends the flagged work back to the employee; <c>Comment</c> tells them what to fix.</summary>
public sealed record RequestReworkDto(string? Comment);

public sealed class QualityFlagFilterDto
{
    /// <summary>OPEN, REWORK_REQUESTED or DISMISSED.</summary>
    public string? Status { get; set; }

    public Guid? EmployeeId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed record QualityFlagDto(
    Guid Id,
    Guid ResultSubmissionId,
    int SubmissionNo,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid FlaggedByEmployeeId,
    string FlaggedByName,
    Guid? QaSampleId,
    string Severity,
    string Reason,
    string Status,
    string? ReworkComment,
    DateTimeOffset? ReworkRequestedAt,
    DateTimeOffset CreatedAt);

public sealed class QaSampleFilterDto
{
    /// <summary>PENDING, PASSED or FAILED.</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed record QaSampleDto(
    Guid Id,
    Guid ResultSubmissionId,
    int SubmissionNo,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string Status,
    DateTimeOffset SampledAt,
    Guid? ReviewedByEmployeeId,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt,
    string? Comment,
    Guid? QualityFlagId);

/// <summary>
/// QA's verdict on a sampled result: <c>Result</c> is PASSED or FAILED. FAILED needs a <c>Comment</c> and raises a quality flag
/// (<c>Severity</c> defaults to MEDIUM); <c>RequestRework</c> also sends the work straight back to the employee.
/// </summary>
public sealed record DecideQaSampleDto(string? Result, string? Comment, string? Severity, bool RequestRework);

public sealed record QaSampleDecisionDto(QaSampleDto Sample, QualityFlagDto? Flag);
