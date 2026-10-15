namespace G54.BLL.Dtos.Submission;

public sealed record WorkSubmittedEvent(
    Guid SubmissionId,
    Guid WorkId,
    Guid SubmitterId,
    int SubmissionNo,
    string WorkCode,
    string WorkTitle,
    DateTimeOffset SubmittedAt);
