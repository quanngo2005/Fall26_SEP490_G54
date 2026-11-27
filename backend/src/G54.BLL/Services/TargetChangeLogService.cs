using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class TargetChangeLogService(AppDbContext db, TimeProvider time) : ITargetChangeLogService
{
    public async Task LogAsync(
        Guid actorId,
        string action,
        int tier,
        Guid kpiId,
        string unit,
        string period,
        decimal? oldValue,
        decimal? newValue,
        string? basis = null,
        Guid? employeeId = null,
        Guid? departmentId = null,
        int? year = null,
        decimal? roundingStep = null,
        CancellationToken cancellationToken = default)
    {
        db.TargetChangeLogs.Add(new TargetChangeLog
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            OccurredAt = time.GetUtcNow(),
            Action = action,
            Tier = tier,
            KpiDefinitionId = kpiId,
            Unit = unit,
            Period = period,
            OldValue = oldValue,
            NewValue = newValue,
            Basis = basis,
            RoundingStep = roundingStep,
            EmployeeId = employeeId,
            DepartmentId = departmentId,
            Year = year
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TargetChangeLog>> GetByEmployeeYearAsync(
        Guid employeeId, int year, CancellationToken cancellationToken = default)
    {
        return await db.TargetChangeLogs
            .AsNoTracking()
            .Include(c => c.KpiDefinition)
            .Where(c => c.EmployeeId == employeeId && c.Year == year)
            .OrderByDescending(c => c.OccurredAt)
            .ToListAsync(cancellationToken);
    }
}
