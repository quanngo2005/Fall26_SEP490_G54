using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Common.Workflow;

/// <summary>What a tier is decided from: the risk events inside a period.</summary>
internal static class RankingInputs
{
    /// <summary>Risk events of the given employees that happened inside the period and were not dismissed, grouped by employee.</summary>
    public static async Task<Dictionary<Guid, List<RiskEvent>>> LoadRiskEventsByEmployeeAsync(
        AppDbContext db,
        List<Guid> employeeIds,
        EvaluationPeriod? period,
        CancellationToken cancellationToken)
    {
        if (period is null || employeeIds.Count == 0)
        {
            return [];
        }

        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var riskEvents = await db.RiskEvents
            .AsNoTracking()
            .Where(re => employeeIds.Contains(re.EmployeeId)
                && re.OccurredAt >= periodStart
                && re.OccurredAt <= periodEnd
                && re.Status != RiskEventStatus.Dismissed)
            .ToListAsync(cancellationToken);

        return riskEvents.GroupBy(re => re.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
    }
}
