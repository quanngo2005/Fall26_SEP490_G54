using G54.BLL.Dtos.TargetAssignment;

namespace G54.BLL.Services;

public interface ITargetAssignmentService
{
    /// <summary>Creates the ACTIVE root target of a KPI and period for a branch. Administrators only.</summary>
    Task<TargetAssignmentDto> CreateRootAsync(Guid accountId, CreateTargetAssignmentDto dto, CancellationToken cancellationToken = default);

    /// <summary>Creates every child of <paramref name="parentAssignmentId"/> ACTIVE in one transaction, split by organisation (ORG).</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the values of the ACTIVE children of <paramref name="parentAssignmentId"/> in one transaction: every changed
    /// child is SUPERSEDED and replaced by Version + 1 (so its feedback starts again); only changed children are re-issued, so unchanged ones keep their feedback; owners not yet allocated are added as new children. Returns the new versions.
    /// </summary>
    Task<IReadOnlyList<TargetAssignmentDto>> ReallocateAsync(Guid accountId, Guid parentAssignmentId, ReallocateTargetDto dto, CancellationToken cancellationToken = default);

    /// <summary>Creates every child of <paramref name="parentAssignmentId"/> ACTIVE in one transaction, split by period.</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> AllocateByPeriodAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetByPeriodDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mid-period change of the ACTIVE target <paramref name="assignmentId"/> (BR-15): freezes the current version
    /// (and its active descendants) as SUPERSEDED with <c>EffectiveTo = effectiveDate - 1</c>, then creates the next
    /// version ACTIVE from <c>effectiveDate</c>. Returns the new version and its re-created direct children.
    /// </summary>
    Task<IReadOnlyList<TargetAssignmentDto>> ReviseAsync(Guid accountId, Guid assignmentId, ReviseTargetAssignmentDto dto, CancellationToken cancellationToken = default);

    /// <summary>Lists target assignments matching optional filters, respecting caller's department scope.</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> ListAsync(Guid accountId, TargetAssignmentQueryDto query, CancellationToken cancellationToken = default);

    Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Working-day breakdown of a target's period, for DMs (via org scope) or the owning employee.</summary>
    Task<TargetAssignmentBreakdownDto> GetBreakdownAsync(Guid accountId, Guid assignmentId, string? granularity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Planned per-month, per-week and per-day target of every ACTIVE target an employee owns in one period.
    /// Admin, a manager over the employee's department, or the employee; computed, not stored.
    /// </summary>
    Task<SuggestedDailyTargetDto> GetSuggestedDailyAsync(Guid accountId, Guid employeeId, Guid evaluationPeriodId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own employee-owned target assignments, with feedbackStatus.</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> GetMineAsync(Guid accountId, MyTargetAssignmentQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>Owner employee confirms the target. 403 if not the owner; 409 if already confirmed after the last update.</summary>
    Task<TargetAssignmentFeedbackDto> ConfirmAsync(Guid accountId, Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Owner employee leaves a comment (<= 500 chars) on the target.</summary>
    Task<TargetAssignmentFeedbackDto> CommentAsync(Guid accountId, Guid assignmentId, CreateTargetFeedbackCommentDto dto, CancellationToken cancellationToken = default);

    /// <summary>Feedback history for a target, newest first. Owner employee or org-scoped caller only.</summary>
    Task<IReadOnlyList<TargetAssignmentFeedbackDto>> GetFeedbackAsync(Guid accountId, Guid assignmentId, CancellationToken cancellationToken = default);
}
