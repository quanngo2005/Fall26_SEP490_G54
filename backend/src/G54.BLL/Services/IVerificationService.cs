using G54.BLL.Dtos.Verification;

namespace G54.BLL.Services;

public interface IVerificationService
{
    Task<VerificationDossierDto?> GetDossierAsync(Guid submissionId, CancellationToken cancellationToken = default);

    Task<VerificationDecisionResultDto> SubmitDecisionAsync(
        Guid submissionId,
        Guid actorAccountId,
        SubmitVerificationDecisionDto dto,
        CancellationToken cancellationToken = default);

    Task<List<VerificationHistoryDto>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken = default);
}
