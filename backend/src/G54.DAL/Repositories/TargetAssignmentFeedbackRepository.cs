using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public sealed class TargetAssignmentFeedbackRepository(AppDbContext dbContext)
    : Repository<TargetAssignmentFeedback>(dbContext), ITargetAssignmentFeedbackRepository
{
    public Task<TargetAssignmentFeedback?> GetLatestAsync(Guid targetAssignmentId, CancellationToken ct = default) =>
        DbSet.AsNoTracking()
            .Where(f => f.TargetAssignmentId == targetAssignmentId)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TargetAssignmentFeedback>> ListForTargetAsync(Guid targetAssignmentId, CancellationToken ct = default) =>
        await DbSet.AsNoTracking()
            .Where(f => f.TargetAssignmentId == targetAssignmentId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, TargetAssignmentFeedback>> GetLatestForTargetsAsync(
        IReadOnlyCollection<Guid> targetAssignmentIds, CancellationToken ct = default)
    {
        if (targetAssignmentIds.Count == 0)
        {
            return new Dictionary<Guid, TargetAssignmentFeedback>();
        }

        var rows = await DbSet.AsNoTracking()
            .Where(f => targetAssignmentIds.Contains(f.TargetAssignmentId))
            .ToListAsync(ct);

        return rows
            .GroupBy(f => f.TargetAssignmentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.CreatedAt).First());
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => DbContext.SaveChangesAsync(ct);
}
