using G54.DAL.Entities;

namespace G54.DAL.Repositories;

public interface ITargetAssignmentRepository : IRepository<TargetAssignment>
{
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
