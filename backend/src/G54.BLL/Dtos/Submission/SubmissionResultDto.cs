namespace G54.BLL.Dtos.Submission;

public sealed record SubmissionResultDto(
    Guid SubmissionId,
    Guid WorkId,
    string WorkCode,
    int SubmissionNo,
    string Status,
    DateTimeOffset SubmittedAt,
    int EvidenceCount,
    int MetricCount,
    string? Comment);
