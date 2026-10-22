using G54.DAL.Entities;

namespace G54.BLL.Common.Constants;

/// <summary>
/// Shared groupings of <see cref="WorkStatus"/> used by reporting queries, so every service agrees on what
/// "open", "completed" or "in execution" means (including <see cref="WorkStatus.Overdue"/>).
/// Declared as arrays so they translate to <c>status = ANY(@p)</c> inside EF queries.
/// </summary>
public static class WorkStatusGroups
{
    /// <summary>Work that still needs action from the assignee or a verifier.</summary>
    public static readonly WorkStatus[] Open =
    [
        WorkStatus.New,
        WorkStatus.InProgress,
        WorkStatus.HandedOver,
        WorkStatus.Submitted,
        WorkStatus.Rejected,
        WorkStatus.Overdue,
    ];

    /// <summary>Work whose result has been verified or the work closed out.</summary>
    public static readonly WorkStatus[] Completed =
    [
        WorkStatus.Verified,
        WorkStatus.Done,
    ];

    /// <summary>Work currently being carried out or awaiting review.</summary>
    public static readonly WorkStatus[] InExecution =
    [
        WorkStatus.InProgress,
        WorkStatus.Submitted,
        WorkStatus.HandedOver,
        WorkStatus.Overdue,
    ];

    /// <summary>Work counting towards an employee's current load (used by load-balanced allocation).</summary>
    public static readonly WorkStatus[] ActiveLoad =
    [
        WorkStatus.New,
        WorkStatus.InProgress,
        WorkStatus.Overdue,
    ];

    /// <summary>Work that is moved to <see cref="WorkStatus.Overdue"/> once its planned end date has passed.</summary>
    public static readonly WorkStatus[] OverdueEligible =
    [
        WorkStatus.New,
        WorkStatus.InProgress,
        WorkStatus.HandedOver,
    ];
}
