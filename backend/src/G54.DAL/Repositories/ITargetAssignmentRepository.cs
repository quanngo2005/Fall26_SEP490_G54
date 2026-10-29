using G54.DAL.Entities;

namespace G54.DAL.Repositories;

public interface ITargetAssignmentRepository : IRepository<TargetAssignment>
{
    /// <summary>
    /// Branch-year target lookup (import upsert). Tracked. Ignores Superseded/Closed.
    /// </summary>
    Task<TargetAssignment?> GetCurrentBranchYearAsync(Guid branchId, Guid kpiId, Guid yearPeriodId, CancellationToken ct = default);

    /// <summary>
    /// Branch-year, dept-year or quarter target by id, tracked, with Department + EvaluationPeriod + KpiDefinition.
    /// </summary>
    Task<TargetAssignment?> GetWithContextAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Current (Draft or Active) direct children of a parent. Tracked.
    /// </summary>
    Task<IReadOnlyList<TargetAssignment>> GetCurrentChildrenAsync(Guid parentId, CancellationToken ct = default);

    /// <summary>
    /// All Active descendants (any depth) of a row. Used by R4 and re-confirm. Tracked.
    /// </summary>
    Task<IReadOnlyList<TargetAssignment>> GetActiveDescendantsAsync(Guid rootId, CancellationToken ct = default);

    /// <summary>
    /// Whole tree for branch + year period. No tracking. Includes KpiDefinition, Department, EvaluationPeriod, ActualProgresses.
    /// </summary>
    Task<IReadOnlyList<TargetAssignment>> GetTreeAsync(Guid branchId, Guid yearPeriodId, CancellationToken ct = default);

    /// <summary>Gets a target assignment with its owning employee included (if any). No tracking; null if not found.</summary>
    Task<TargetAssignment?> GetWithOwnerAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lists target assignments matching optional filters, sorted by evaluationPeriodId, then parentAssignmentId (null first), then createdAt.
    /// </summary>
    /// <param name="ownerDepartmentIds">Restrict to these owning departments (for employees, their department). Null = no restriction (admin).</param>
    Task<IReadOnlyList<TargetAssignment>> ListAsync(
        Guid? kpiId,
        Guid? periodId,
        TargetAssignmentStatus? status,
        IReadOnlyCollection<Guid>? ownerDepartmentIds,
        CancellationToken ct = default);

    /// <summary>Gets all descendant department IDs (includes the given department itself).</summary>
    Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(Guid departmentId, CancellationToken ct = default);

    /// <summary>Department of the account's employee; null if the account is unknown.</summary>
    Task<Guid?> GetAccountDepartmentIdAsync(Guid accountId, CancellationToken ct = default);

    Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
