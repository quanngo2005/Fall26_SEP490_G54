namespace G54.BLL.Dtos.Verification;

public sealed record VerificationDecisionResultDto(
    Guid VerificationId,
    string Decision,
    string SubmissionStatus,
    string WorkStatus,
    DateTimeOffset VerifiedAt,
    string Message);
