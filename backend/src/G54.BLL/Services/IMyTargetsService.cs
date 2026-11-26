using G54.BLL.Dtos.TargetAllocation;

namespace G54.BLL.Services;

/// <summary>
/// Employee-facing "my-targets" flow for Target Allocation v3 (BR-36): own year/quarter targets, year-level
/// confirmation (ack), feedback with an optional proposed value, DM reply, and quarter progress.
/// Contract: docs/api/TARGET_ALLOCATION_CONTRACT.md "Employee side".
/// </summary>
public interface IMyTargetsService
{
    /// <summary>The caller's own KPI targets for the year: year + 4 quarters per KPI, last year's target/actual,
    /// ack status, and which KPIs changed (with old/new value) since the employee last confirmed.</summary>
    Task<MyTargetsDto> GetMyTargetsAsync(Guid userId, int year, CancellationToken cancellationToken = default);

    /// <summary>Confirms (or reconfirms) the caller's targets for the year. Throws <see cref="G54.BLL.Common.Exceptions.ConflictException"/>
    /// (409 TARGET_ACK_INVALID_STATE) when the ack is not PENDING/RECONFIRM.</summary>
    Task<ConfirmMyTargetsResultDto> ConfirmAsync(Guid userId, int year, CancellationToken cancellationToken = default);

    /// <summary>Submits feedback on one of the caller's own KPIs, with an optional proposed value. A cumulative
    /// (SUM) proposal's quarters must sum to the proposed year (422 TARGET_FEEDBACK_PROPOSED_SUM_MISMATCH). The
    /// reason is required and limited to 500 characters (400 MAX_LENGTH_EXCEEDED, MSG08-style).</summary>
    Task<TargetFeedbackDto> SubmitFeedbackAsync(
        Guid userId, int year, SubmitTargetFeedbackRequest request, CancellationToken cancellationToken = default);

    /// <summary>DM reply to an employee's feedback; scoped to the DM's own department (403 FORBIDDEN otherwise).</summary>
    Task<TargetFeedbackDto> ReplyFeedbackAsync(
        Guid userId, Guid feedbackId, ReplyTargetFeedbackRequest request, CancellationToken cancellationToken = default);

    /// <summary>Quarter/year progress for the caller: achieved counts only auto-reconciled results (ActualProgress),
    /// with the still-pending (IN_REVIEW) work transaction count reported separately, plus expected pace (D16).</summary>
    Task<MyTargetsProgressDto> GetProgressAsync(
        Guid userId, int year, string? period, CancellationToken cancellationToken = default);

    /// <summary>DM listing of employee feedback for a department/year (W9 panel, W11 drawer), optionally narrowed
    /// to one employee. Scoped to the DM's own department (403 FORBIDDEN otherwise).</summary>
    Task<IReadOnlyList<TargetFeedbackDto>> GetFeedbackAsync(
        Guid userId, int year, Guid departmentId, Guid? employeeId, CancellationToken cancellationToken = default);

    /// <summary>Change-log history for one employee/year, optionally narrowed to one KPI (W11 drawer). Allowed for
    /// the employee themselves, or their DM (target:manage at Department scope); 403 FORBIDDEN otherwise.</summary>
    Task<IReadOnlyList<TargetChangeLogEntryDto>> GetChangeLogAsync(
        Guid userId, int year, Guid employeeId, Guid? kpiId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own feedback history (with any DM reply) for the year, optionally narrowed to one KPI (E5).</summary>
    Task<IReadOnlyList<TargetFeedbackDto>> GetMyFeedbackAsync(
        Guid userId, int year, Guid? kpiId, CancellationToken cancellationToken = default);

    /// <summary>The reconciled (or still-pending) transactions behind the caller's own result for one KPI/year
    /// (E5): date, transaction code, CIF/refer id, amount, and AUTO_RECONCILED/NOT_MATCHED/IN_REVIEW status.</summary>
    Task<IReadOnlyList<MyContributionDto>> GetMyContributionsAsync(
        Guid userId, int year, Guid kpiId, CancellationToken cancellationToken = default);
}
