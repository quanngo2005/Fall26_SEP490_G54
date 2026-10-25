using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public sealed class EvaluationPeriodRepository(AppDbContext dbContext) : Repository<EvaluationPeriod>(dbContext), IEvaluationPeriodRepository
{
    public async Task<EvaluationPeriod?> GetYearAsync(int year, CancellationToken ct = default)
    {
        return await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == year.ToString(System.Globalization.CultureInfo.InvariantCulture) && p.PeriodType == PeriodType.Year, ct);
    }

    public async Task<IReadOnlyList<EvaluationPeriod>> GetInRangeAsync(PeriodType type, DateOnly start, DateOnly endDate, CancellationToken ct = default)
    {
        return await DbSet
            .AsNoTracking()
            .Where(p => p.PeriodType == type && p.StartDate >= start && p.EndDate <= endDate)
            .OrderBy(p => p.StartDate)
            .ToListAsync(ct);
    }
}
