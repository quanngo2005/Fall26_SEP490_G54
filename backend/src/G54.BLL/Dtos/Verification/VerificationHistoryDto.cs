namespace G54.BLL.Dtos.Verification;

public sealed record VerificationHistoryDto(
    Guid VerificationId,
    Guid ResultSubmissionId,
    int RoundNo,
    string Decision,
    string? Comment,
    Guid VerifierEmployeeId,
    string VerifierFullName,
    DateTimeOffset? VerifiedAt);
