using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

/// <summary>
/// Read-only from the API's point of view (<see cref="AddRangeAsync"/> exists for seeders/tests only).
/// The <see cref="Auditing.AuditSaveChangesInterceptor"/> writes audit rows directly through the DbContext
/// instead of this repository, to avoid an interceptor &lt;-&gt; DbContext dependency cycle.
/// </summary>
public sealed class AuditLogRepository(AppDbContext dbContext) : IAuditLogRepository
{
    public async Task<(List<AuditLog> Items, int TotalCount)> GetPagedAsync(
        AuditLogFilter filter,
        int page,
        int pageSize,
        bool sortDesc,
        CancellationToken ct = default)
    {
        var query = dbContext.Set<AuditLog>().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.EntityName))
        {
            query = query.Where(a => a.EntityName == filter.EntityName);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(a => a.EntityId == filter.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(a => a.Action == filter.Action);
        }

        if (filter.ActorId.HasValue)
        {
            query = query.Where(a => a.ActorId == filter.ActorId.Value);
        }

        if (filter.From.HasValue)
        {
            query = query.Where(a => a.OccurredAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(a => a.OccurredAt <= filter.To.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = $"%{filter.Keyword}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.EntityName, keyword) ||
                EF.Functions.ILike(a.EntityId, keyword) ||
                (a.ActorName != null && EF.Functions.ILike(a.ActorName, keyword)));
        }

        var totalCount = await query.CountAsync(ct);

        query = sortDesc ? query.OrderByDescending(a => a.OccurredAt) : query.OrderBy(a => a.OccurredAt);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Set<AuditLog>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddRangeAsync(IEnumerable<AuditLog> logs, CancellationToken ct = default)
    {
        await dbContext.Set<AuditLog>().AddRangeAsync(logs, ct);
        await dbContext.SaveChangesAsync(ct);
    }
}
