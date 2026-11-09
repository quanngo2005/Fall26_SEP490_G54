using G54.BLL.Common.Models;
using G54.BLL.Dtos.Appeal;

namespace G54.BLL.Services;

/// <summary>Appeals against rejected result submissions. Contract: docs/api/APPEAL_CONTRACT.md.</summary>
public interface IAppealService
{
    Task<ScoreEntryDto> ReviewAppealAsync(Guid appealId, Guid userId, ReviewAppealRequestDto request, CancellationToken cancellationToken = default);
    Task<ScoreEntryDto> DeductTaskScoreAsync(Guid workId, Guid userId, ReviewAppealRequestDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScoreEntryDto>> GetScoreEntriesAsync(Guid appealId, Guid userId, CancellationToken cancellationToken = default);
    Task<AppealDetailDto> DisputeScoreAsync(Guid appealId, Guid userId, ScoreDisputeRequestDto request, CancellationToken cancellationToken = default);
    Task<AppealScoreSettingDto> GetScoreSettingAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AppealScoreSettingDto> UpdateScoreSettingAsync(Guid userId, AppealScoreSettingDto request, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Files an appeal against the caller's own calculated score (<c>CALCULATED</c> or <c>IN_REVIEW</c>, period not locked),
    /// within the configured hours after the score was calculated. The score becomes <c>DISPUTED</c> until the appeal ends.
    /// </summary>
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">No such evaluation.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The evaluation belongs to someone else.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ConflictException">
    /// Period locked (<c>PERIOD_LOCKED</c>), score not appealable or already decided (<c>APPEAL_INVALID_STATE</c>),
    /// or an appeal is in progress (<c>APPEAL_ALREADY_OPEN</c>).
    /// </exception>
    /// <exception cref="G54.BLL.Common.Exceptions.BusinessRuleException">The window has passed (<c>APPEAL_SCORE_WINDOW_EXPIRED</c>).</exception>
    Task<AppealDetailDto> CreateScoreAppealAsync(
        Guid evaluationId,
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
