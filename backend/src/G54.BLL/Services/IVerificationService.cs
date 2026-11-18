using G54.BLL.Common.Models;
using G54.BLL.Dtos.Verification;

namespace G54.BLL.Services;

public interface IVerificationService
{
    /// <summary>
    /// Submissions waiting for a decision (SUBMITTED / UNDER_REVIEW) from departments the caller's
    /// <c>verification:decide</c> scope covers, oldest first. The caller's own submissions are left out.
    /// </summary>
    Task<PagedResult<VerificationQueueItemDto>> GetQueueAsync(
        Guid actorAccountId,
        VerificationQueueFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<VerificationDossierDto> GetDossierAsync(Guid submissionId, Guid actorAccountId, CancellationToken cancellationToken = default);

    Task<VerificationDecisionResultDto> SubmitDecisionAsync(
        Guid submissionId,
        Guid actorAccountId,
        SubmitVerificationDecisionDto dto,
        CancellationToken cancellationToken = default);

    Task<List<VerificationHistoryDto>> GetHistoryAsync(Guid submissionId, Guid actorAccountId, CancellationToken cancellationToken = default);
}
