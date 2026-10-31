using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public sealed class TargetAssignmentRepository(AppDbContext dbContext) : Repository<TargetAssignment>(dbContext), ITargetAssignmentRepository
{
    public async Task<TargetAssignment?> GetCurrentBranchYearAsync(Guid branchId, Guid kpiId, Guid yearPeriodId, CancellationToken ct = default)
    {
        return await DbSet
            .Where(t => t.DepartmentId == branchId
                     && t.KpiDefinitionId == kpiId
                     && t.EvaluationPeriodId == yearPeriodId
                     && t.Status != TargetAssignmentStatus.Superseded
                     && t.Status != TargetAssignmentStatus.Closed)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TargetAssignment?> GetWithContextAsync(Guid id, CancellationToken ct = default)
    {
        return await DbSet
            .Include(t => t.Department)
            .Include(t => t.EvaluationPeriod)
            .Include(t => t.KpiDefinition)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<IReadOnlyList<TargetAssignment>> GetCurrentChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        return await DbSet
            .Include(t => t.EvaluationPeriod)
            .Where(t => t.ParentAssignmentId == parentId
                     && (t.Status == TargetAssignmentStatus.Draft || t.Status == TargetAssignmentStatus.Active))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TargetAssignment>> GetActiveDescendantsAsync(Guid rootId, CancellationToken ct = default)
    {
        var result = new List<TargetAssignment>();
        var ids = new List<Guid?> { rootId };

        while (ids.Count > 0)
        {
            var children = await DbSet
                .Where(t => ids.Contains(t.ParentAssignmentId) && t.Status == TargetAssignmentStatus.Active)
                .ToListAsync(ct);

            if (children.Count == 0)
                break;

            result.AddRange(children);
            ids = children.Select(c => (Guid?)c.Id).ToList();
        }

        return result;
    }

    public async Task<IReadOnlyList<TargetAssignment>> GetTreeAsync(Guid branchId, Guid yearPeriodId, CancellationToken ct = default)
    {
        var l1Ids = await DbSet
            .Where(t => t.DepartmentId == branchId
                     && t.ParentAssignmentId == null
                     && t.EvaluationPeriodId == yearPeriodId
                     && t.Status != TargetAssignmentStatus.Superseded)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var result = new List<TargetAssignment>();
        var currentIds = l1Ids;

        for (int level = 0; level < 5; level++)
        {
            var rows = await DbSet
                .AsNoTracking()
                .Include(t => t.KpiDefinition)
                .Include(t => t.Department)
                .Include(t => t.EvaluationPeriod)
                .Include(t => t.ActualProgresses)
                .Where(t => currentIds.Contains(t.Id))
                .ToListAsync(ct);

            result.AddRange(rows);

            if (level < 4)
            {
                var parentIds = rows.Select(r => (Guid?)r.Id).ToList();
                currentIds = await DbSet
                    .Where(t => parentIds.Contains(t.ParentAssignmentId))
                    .Select(t => t.Id)
                    .ToListAsync(ct);
            }

            if (currentIds.Count == 0)
                break;
        }

        return result;
    }

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
