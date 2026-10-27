using G54.BLL.Common.Models;
using G54.BLL.Dtos.Appeal;

namespace G54.BLL.Services;

/// <summary>Appeals against rejected result submissions. Contract: docs/api/APPEAL_CONTRACT.md.</summary>
public interface IAppealService
{
    /// <summary>Resolves an appeal; ACCEPT approves the submission, ADJUST requests revision, REJECT keeps it rejected.</summary>
    Task<AppealDecisionResultDto> DecideAppealAsync(
        Guid appealId,
        Guid userId,
        DecideAppealRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Files an appeal for a rejected submission. Only the submitter may appeal, within 48 hours of the rejection
    /// (server time), and only while the submission has no appeal in progress.
    /// </summary>
    /// <exception cref="G54.BLL.Common.Exceptions.ValidationException">Blank or too long reason, or invalid files.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">No such submission.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The caller did not submit the result.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ConflictException">
    /// The submission is not rejected (<c>APPEAL_INVALID_STATE</c>) or already has an appeal in progress (<c>APPEAL_ALREADY_OPEN</c>).
    /// </exception>
    /// <exception cref="G54.BLL.Common.Exceptions.BusinessRuleException">The 48-hour window has passed (<c>APPEAL_WINDOW_EXPIRED</c>).</exception>
    Task<AppealDetailDto> CreateAppealAsync(
        Guid submissionId,
        Guid userId,
        CreateAppealRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws an appeal that is still OPEN. Only the employee who filed it may withdraw.</summary>
    Task<AppealDetailDto> WithdrawAppealAsync(Guid appealId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Lists the appeals the caller may see: own, or those within the scope of the caller's verification permission.</summary>
    Task<PagedResult<AppealListItemDto>> GetAppealsAsync(
        Guid userId,
        AppealFilterDto filter,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one appeal when it is within the caller's visibility.</summary>
    Task<AppealDetailDto> GetAppealByIdAsync(Guid appealId, Guid userId, CancellationToken cancellationToken = default);
}
