using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public sealed class TargetAssignmentRepository(AppDbContext dbContext)
    : Repository<TargetAssignment>(dbContext), ITargetAssignmentRepository
{
    public Task<TargetAssignment?> GetWithOwnerAsync(Guid id, CancellationToken ct = default) =>
        DbSet.AsNoTracking()
            .Include(t => t.Employee)
            .SingleOrDefaultAsync(t => t.Id == id, ct);

    public Task<Guid?> GetAccountDepartmentIdAsync(Guid accountId, CancellationToken ct = default) =>
        DbContext.Set<Account>().AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => (Guid?)a.Employee.DepartmentId)
            .SingleOrDefaultAsync(ct);

    public Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct = default) =>
        DbContext.Set<Department>().AnyAsync(d => d.Id == departmentId, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => DbContext.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<TargetAssignment>> ListAsync(
        Guid? kpiId,
        Guid? periodId,
        TargetAssignmentStatus? status,
        IReadOnlyCollection<Guid>? ownerDepartmentIds,
        CancellationToken ct = default)
    {
        var query = DbSet
            .AsNoTracking()
            .Include(t => t.Employee)
            .AsQueryable();

        if (kpiId.HasValue)
        {
            query = query.Where(t => t.KpiDefinitionId == kpiId.Value);
        }

        if (periodId.HasValue)
        {
            query = query.Where(t => t.EvaluationPeriodId == periodId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        if (ownerDepartmentIds is not null)
        {
            query = query.Where(t =>
                ownerDepartmentIds.Contains(t.DepartmentId ?? t.Employee!.DepartmentId));
        }

        return await query
            .OrderBy(t => t.EvaluationPeriodId)
            .ThenBy(t => t.ParentAssignmentId)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(Guid departmentId, CancellationToken ct = default)
    {
        var result = new List<Guid> { departmentId };
        var queue = new Queue<Guid>();
        queue.Enqueue(departmentId);

        var allDepartments = await DbContext.Set<Department>()
            .AsNoTracking()
            .Select(d => new { d.Id, d.ParentDepartmentId })
            .ToListAsync(ct);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = allDepartments.Where(d => d.ParentDepartmentId == current).Select(d => d.Id).ToList();

            foreach (var child in children)
            {
                if (!result.Contains(child))
                {
                    result.Add(child);
                    queue.Enqueue(child);
                }
            }
        }

        return result.AsReadOnly();
    }
}
