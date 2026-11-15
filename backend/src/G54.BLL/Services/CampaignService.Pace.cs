using G54.BLL.Common.Constants;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Campaign;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Pace and KPI figures of the campaign list for the SCR-PRF-03 campaign card (docs/api/BRANCH_DASHBOARD_CONTRACT.md §3).
/// </summary>
public sealed partial class CampaignService
{
    /// <summary>Completed works / all works × 100 (1 decimal); 0 without works. Same rule as the detail metrics.</summary>
    public static decimal ProgressPercent(int totalWorks, int completedWorks) =>
        totalWorks > 0 ? Math.Round((decimal)completedWorks / totalWorks * 100m, 1) : 0m;

    /// <summary>Share of the campaign's days that have started by <paramref name="today"/>, clamped to 0..100 (1 decimal).</summary>
    public static decimal TimeElapsedPercent(DateOnly startDate, DateOnly endDate, DateOnly today)
    {
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
        if (totalDays <= 0)
        {
            return 100m;
        }

        var elapsed = (decimal)(today.DayNumber - startDate.DayNumber + 1) / totalDays * 100m;
        return Math.Round(Math.Clamp(elapsed, 0m, 100m), 1);
    }

    private static async Task<int> CountSlowCampaignsAsync(
        IQueryable<BusinessCampaign> query,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .Select(c => new
            {
                c.StartDate,
                c.EndDate,
                Total = c.Works.Count,
                Completed = c.Works.Count(w => WorkStatusGroups.Completed.Contains(w.Status)),
            })
            .ToListAsync(cancellationToken);

        return rows.Count(r => ProgressPercent(r.Total, r.Completed) - TimeElapsedPercent(r.StartDate, r.EndDate, today) < 0m);
    }

    /// <summary>Adds progress, pace and the campaign KPI (target / actual) to a page of campaigns.</summary>
    private async Task<List<CampaignSummaryDto>> AddPaceAsync(
        List<CampaignSummaryDto> items,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(c => c.Id).ToList();
        var kpiOf = await ResolveCampaignKpisAsync(ids, today, cancellationToken);

        var targets = await db.Works.AsNoTracking()
            .Where(w => w.BusinessCampaignId != null && ids.Contains(w.BusinessCampaignId.Value) && w.TargetValue != null)
            .GroupBy(w => w.BusinessCampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Target = g.Sum(w => w.TargetValue!.Value) })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Target, cancellationToken);

        var actualItems = await db.WorkResultItems.AsNoTracking()
            .Where(i => i.VerifiedWorkResult.Status == VerifiedResultStatus.Valid)
            .Select(i => new
            {
                CampaignId = i.VerifiedWorkResult.Verification != null
                    ? i.VerifiedWorkResult.Verification.ResultSubmission.Work.BusinessCampaignId
                    : i.VerifiedWorkResult.WorkTransaction != null
                        ? i.VerifiedWorkResult.WorkTransaction.Work.BusinessCampaignId
                        : null,
                i.ActualProgress.TargetAssignment.KpiDefinitionId,
                i.RawValue,
            })
            .Where(x => x.CampaignId != null && ids.Contains(x.CampaignId.Value))
            .ToListAsync(cancellationToken);

        return items
            .Select(c =>
            {
                var progress = ProgressPercent(c.GeneratedWorksCount, c.CompletedWorksCount);
                var elapsed = TimeElapsedPercent(c.StartDate, c.EndDate, today);
                var kpi = kpiOf.GetValueOrDefault(c.Id);
                return c with
                {
                    ProgressPercent = progress,
                    TimeElapsedPercent = elapsed,
                    PaceGap = Math.Round(progress - elapsed, 1),
                    KpiCode = kpi?.Code,
                    KpiName = kpi?.Name,
                    TargetUnit = kpi?.Unit,
                    TargetValue = kpi is not null && targets.TryGetValue(c.Id, out var target) ? target : null,
                    ActualValue = kpi is null
                        ? null
                        : actualItems
                            .Where(x => x.CampaignId == c.Id && x.KpiDefinitionId == kpi.Id)
                            .Sum(x => x.RawValue),
                };
            })
            .ToList();
    }

    /// <summary>
    /// The KPI each campaign is measured by: the KPI of its work types (effective today, required first, then earliest),
    /// only when every work type of the campaign maps to the same one.
    /// </summary>
    private async Task<Dictionary<Guid, CampaignKpi>> ResolveCampaignKpisAsync(
        List<Guid> campaignIds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var campaignTypes = await db.CampaignWorkTypes.AsNoTracking()
            .Where(cwt => campaignIds.Contains(cwt.BusinessCampaignId))
            .Select(cwt => new { cwt.BusinessCampaignId, cwt.WorkTypeId })
            .ToListAsync(cancellationToken);

        var workTypeIds = campaignTypes.Select(x => x.WorkTypeId).Distinct().ToList();
        var typeKpis = (await db.WorkTypeKpis.AsNoTracking()
                .Where(k => workTypeIds.Contains(k.WorkTypeId)
                    && k.EffectiveFrom <= today
                    && (k.EffectiveTo == null || k.EffectiveTo >= today))
                .OrderByDescending(k => k.IsRequired).ThenBy(k => k.EffectiveFrom).ThenBy(k => k.Id)
                .Select(k => new
                {
                    k.WorkTypeId,
                    Kpi = new CampaignKpi(k.KpiDefinitionId, k.KpiDefinition.Code, k.KpiDefinition.Name, k.KpiDefinition.Unit),
                })
                .ToListAsync(cancellationToken))
            .GroupBy(k => k.WorkTypeId)
            .ToDictionary(g => g.Key, g => g.First().Kpi);

        var result = new Dictionary<Guid, CampaignKpi>();
        foreach (var campaign in campaignTypes.GroupBy(x => x.BusinessCampaignId))
        {
            var kpis = campaign.Select(x => typeKpis.GetValueOrDefault(x.WorkTypeId)).ToList();
            if (kpis.All(k => k is not null) && kpis.Select(k => k!.Id).Distinct().Count() == 1)
            {
                result[campaign.Key] = kpis[0]!;
            }
        }

        return result;
    }

    private sealed record CampaignKpi(Guid Id, string Code, string Name, string Unit);
}
