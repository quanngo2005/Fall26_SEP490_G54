using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class HandoverEscalationService(AppDbContext db, TimeProvider time) : IHandoverEscalationService
{
    public async Task<int> EscalateOverdueAsync(TimeSpan sla, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var cutoff = now - sla;

        var overdue = await db.TaskHandovers
            .Include(h => h.ToEmployee)
            .Where(h => h.Status == HandoverStatus.Pending
                && h.EscalatedAt == null
                && h.HandoverAt <= cutoff
                && h.ToEmployee.ManagerId != null)
            .ToListAsync(cancellationToken);

        foreach (var handover in overdue)
        {
            handover.EscalatedAt = now;
            handover.EscalatedToEmployeeId = handover.ToEmployee.ManagerId;
            handover.UpdatedAt = now;
        }

        if (overdue.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return overdue.Count;
    }
}
