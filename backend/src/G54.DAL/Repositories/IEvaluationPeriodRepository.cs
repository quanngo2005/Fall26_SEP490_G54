using G54.DAL.Entities;

namespace G54.DAL.Repositories;

public interface IEvaluationPeriodRepository : IRepository<EvaluationPeriod>
{
    /// <summary>
    /// Get YEAR period by code (year as string). AsNoTracking.
    /// </summary>
    Task<EvaluationPeriod?> GetYearAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// Get periods of a type within a date range, ordered by StartDate. AsNoTracking.
    /// </summary>
    Task<IReadOnlyList<EvaluationPeriod>> GetInRangeAsync(PeriodType type, DateOnly start, DateOnly endDate, CancellationToken ct = default);
}
