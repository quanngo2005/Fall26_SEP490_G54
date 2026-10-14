namespace G54.BLL.Dtos.Submission;

public sealed record SubmissionDetailDto(
    Guid Id,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    Guid SubmitterEmployeeId,
    string SubmitterEmployeeName,
    string SubmitterEmployeeCode,
    int SubmissionNo,
    string Status,
    string? Comment,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SubmissionEvidenceDto> Evidences,
    IReadOnlyList<SubmissionMetricItemDto> Metrics);

public sealed record SubmissionEvidenceDto(
    Guid Id,
    string FileName,
    string FilePath,
    string? MimeType,
    long? FileSize,
    DateTimeOffset UploadedAt);

public sealed record SubmissionMetricItemDto(
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string Unit,
    decimal Value,
    decimal? Denominator);
