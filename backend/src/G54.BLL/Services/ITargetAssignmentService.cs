using G54.BLL.Dtos.TargetAssignment;

namespace G54.BLL.Services;

public interface ITargetAssignmentService
{
    /// <summary>Creates the ACTIVE root target of a KPI and period for a branch. Administrators only.</summary>
    Task<TargetAssignmentDto> CreateRootAsync(Guid accountId, CreateTargetAssignmentDto dto);

    /// <summary>Creates every child of <paramref name="parentAssignmentId"/> ACTIVE in one transaction, split by organisation (ORG).</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto);

    /// <summary>Creates every child of <paramref name="parentAssignmentId"/> ACTIVE in one transaction, split by period.</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> AllocateByPeriodAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetByPeriodDto dto);

    /// <summary>Lists target assignments matching optional filters, respecting caller's department scope.</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> ListAsync(Guid accountId, TargetAssignmentQueryDto query);

    Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId);

    /// <summary>Working-day breakdown of a target's period, for DMs (via org scope) or the owning employee.</summary>
    Task<TargetAssignmentBreakdownDto> GetBreakdownAsync(Guid accountId, Guid assignmentId, string? granularity);

    /// <summary>The caller's own employee-owned target assignments, with feedbackStatus.</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> GetMineAsync(Guid accountId, MyTargetAssignmentQueryDto query);

    /// <summary>Owner employee confirms the target. 403 if not the owner; 409 if already confirmed after the last update.</summary>
    Task<TargetAssignmentFeedbackDto> ConfirmAsync(Guid accountId, Guid assignmentId);

    /// <summary>Owner employee leaves a comment (<= 500 chars) on the target.</summary>
    Task<TargetAssignmentFeedbackDto> CommentAsync(Guid accountId, Guid assignmentId, CreateTargetFeedbackCommentDto dto);

    /// <summary>Feedback history for a target, newest first. Owner employee or org-scoped caller only.</summary>
    Task<IReadOnlyList<TargetAssignmentFeedbackDto>> GetFeedbackAsync(Guid accountId, Guid assignmentId);
}
