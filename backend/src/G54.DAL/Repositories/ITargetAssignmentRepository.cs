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
}
