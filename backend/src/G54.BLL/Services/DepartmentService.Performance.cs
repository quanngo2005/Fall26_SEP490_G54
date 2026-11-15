using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Department;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// SCR-PRF-02 / SCR-PRF-03 figures of the department overview and members: compare period, KPI groups, profile group
/// scores, trend, attention list and child units (docs/api/DEPARTMENT_CONTRACT.md, BRANCH_DASHBOARD_CONTRACT.md).
/// </summary>
public sealed partial class DepartmentService
{
    // ----- Compare period -------------------------------------------------------------------

    /// <summary>
    /// The period the selected one is compared with: the latest earlier period of the same type (PREVIOUS) or the same
    /// type starting one year earlier (SAME_LAST_YEAR). Null when there is none.
    /// </summary>
    public static EvaluationPeriod? ResolveComparePeriod(
        EvaluationPeriod? period,
        IEnumerable<EvaluationPeriod> selectable,
        PerformanceCompareMode mode)
    {
        if (period is null)
        {
            return null;
        }

        var sameType = selectable.Where(p => p.PeriodType == period.PeriodType && p.Id != period.Id);
        return mode == PerformanceCompareMode.SameLastYear
            ? sameType.FirstOrDefault(p => p.StartDate == period.StartDate.AddYears(-1))
            : sameType
                .Where(p => p.EndDate < period.StartDate)
                .OrderByDescending(p => p.EndDate)
                .FirstOrDefault();
    }

    private static PerformanceCompareMode ParseCompare(string? value) =>
        ParseEnum<PerformanceCompareMode>(value, "compare") ?? PerformanceCompareMode.Previous;

    // ----- Snapshot of the scores of a subtree ----------------------------------------------

    /// <summary>
    /// Everything the performance cards need about the non-terminated employees of a subtree, loaded with a fixed
    /// number of queries: their scored evaluations in the period and the compare period, their group scores and the
    /// effective evaluation profile of each job position.
    /// </summary>
    private async Task<ScoreSnapshot> LoadSnapshotAsync(
        List<Guid> subtree,
        EvaluationPeriod? period,
        EvaluationPeriod? comparePeriod,
        CancellationToken cancellationToken)
    {
        var employees = await db.Employees.AsNoTracking()
            .Where(e => subtree.Contains(e.DepartmentId) && e.Status != EmployeeStatus.Terminated)
            .Select(e => new SnapshotEmployee(
                e.Id, e.EmployeeCode, e.FullName, e.DepartmentId, e.JobPositionId, e.JobPosition.Title))
            .ToListAsync(cancellationToken);

        if (period is null)
        {
            return new ScoreSnapshot(employees, [], [], [], []);
        }

        var ids = employees.Select(e => e.Id).ToList();
        var current = await LoadScoredAsync(period.Id, ids, cancellationToken);
        var previous = comparePeriod is null
            ? []
            : (await LoadScoredAsync(comparePeriod.Id, ids, cancellationToken)).ToDictionary(x => x.Key, x => x.Value.Score);

        var positionProfiles = await LoadPositionProfilesAsync(
            employees.Select(e => e.JobPositionId).Distinct().ToList(), period, cancellationToken);
        var trees = await LoadProfileTreesAsync(
            current.Values.Select(s => s.ProfileId).Concat(positionProfiles.Values), cancellationToken);
        var groupScores = await LoadGroupScoresAsync(current.Values, trees, cancellationToken);
        var positionRoots = positionProfiles.ToDictionary(
            x => x.Key,
            x => trees.GetValueOrDefault(x.Value) ?? []);

        return new ScoreSnapshot(employees, current, previous, groupScores, positionRoots);
    }

    /// <summary>Scored evaluations of the period by employee (BR-31: disputed and draft scores are left out).</summary>
    private async Task<Dictionary<Guid, ScoredRow>> LoadScoredAsync(
        Guid periodId,
        List<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.PerformanceEvaluations.AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId && employeeIds.Contains(pe.EmployeeId))
            .Where(PerformanceEvaluationExtensions.HasRankableScore)
            .Select(pe => new ScoredRow(pe.Id, pe.EmployeeId, pe.EvaluationProfileId, pe.TotalScore!.Value))
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>
    /// The ACTIVE profile of each job position effective in the period (highest version wins). Positions without one
    /// have no entry.
    /// </summary>
    private async Task<Dictionary<Guid, Guid>> LoadPositionProfilesAsync(
        List<Guid> positionIds,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var profiles = await db.EvaluationProfiles.AsNoTracking()
            .Where(p => positionIds.Contains(p.JobPositionId)
                && p.Status == EvaluationProfileStatus.Active
                && p.EffectiveFrom <= period.EndDate
                && (p.EffectiveTo == null || p.EffectiveTo >= period.StartDate))
            .Select(p => new { p.Id, p.JobPositionId, p.Version })
            .ToListAsync(cancellationToken);

        return profiles
            .GroupBy(p => p.JobPositionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Version).First().Id);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<ProfileNode>>> LoadProfileTreesAsync(
        IEnumerable<Guid> profileIds,
        CancellationToken cancellationToken)
    {
        var ids = profileIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var items = await db.EvaluationProfileItems.AsNoTracking()
            .Where(i => ids.Contains(i.EvaluationProfileId))
            .Include(i => i.KpiDefinition)
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(i => i.EvaluationProfileId)
            .ToDictionary(g => g.Key, g => EvaluationProfileTree.Build(g));
    }

    /// <summary>
    /// Each scored employee's root group scores, computed like <c>groups[].score</c> of the personal dashboard:
    /// <see cref="EvaluationProfileTree.ScoreNode"/> over the stored KPI contributions of the evaluation.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<GroupScore>>> LoadGroupScoresAsync(
        IEnumerable<ScoredRow> scored,
        Dictionary<Guid, IReadOnlyList<ProfileNode>> trees,
        CancellationToken cancellationToken)
    {
        var rows = scored.ToList();
        var evaluationIds = rows.Select(r => r.EvaluationId).ToList();
        if (evaluationIds.Count == 0)
        {
            return [];
        }

        var contributions = await db.PerformanceContributions.AsNoTracking()
            .Where(c => c.PerformanceEvaluationId != null
                && evaluationIds.Contains(c.PerformanceEvaluationId.Value)
                && c.RiskEventId == null
                && c.ActualProgressId != null)
            .Select(c => new
            {
                EvaluationId = c.PerformanceEvaluationId!.Value,
                c.ActualProgress!.TargetAssignment.KpiDefinitionId,
                c.ComponentScore,
            })
            .ToListAsync(cancellationToken);
        var byEvaluationKpi = contributions.ToLookup(c => (c.EvaluationId, c.KpiDefinitionId));

        var result = new Dictionary<Guid, IReadOnlyList<GroupScore>>();
        foreach (var row in rows)
        {
            if (!trees.TryGetValue(row.ProfileId, out var roots))
            {
                continue;
            }

            result[row.EmployeeId] = roots
                .Select(root => new GroupScore(
                    EvaluationProfileTree.NodeName(root),
                    RoundOrNull(EvaluationProfileTree.ScoreNode(
                        root,
                        leaf => byEvaluationKpi[(row.EvaluationId, leaf.Item.KpiDefinitionId!.Value)]
                            .FirstOrDefault()?.ComponentScore))))
                .ToList();
        }

        return result;
    }

    // ----- Performance sections -------------------------------------------------------------

    private async Task<List<DepartmentTrendPointDto>> BuildTrendAsync(
        EvaluationPeriod period,
        List<EvaluationPeriod> selectable,
        ScoreSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var trendPeriods = selectable
            .Where(p => p.PeriodType == period.PeriodType && p.StartDate <= period.StartDate)
            .OrderByDescending(p => p.StartDate)
            .Take(Math.Max(parameters.Value.PerfTrendPeriodCount, 1))
            .Reverse()
            .ToList();

        var periodIds = trendPeriods.Select(p => p.Id).ToList();
        var employeeIds = snapshot.Employees.Select(e => e.Id).ToList();
        var scores = await db.PerformanceEvaluations.AsNoTracking()
            .Where(pe => periodIds.Contains(pe.EvaluationPeriodId) && employeeIds.Contains(pe.EmployeeId))
            .Where(PerformanceEvaluationExtensions.HasRankableScore)
            .Select(pe => new { pe.EvaluationPeriodId, Score = pe.TotalScore!.Value })
            .ToListAsync(cancellationToken);
        var byPeriod = scores.ToLookup(s => s.EvaluationPeriodId, s => s.Score);

        return trendPeriods
            .Select(p => new DepartmentTrendPointDto(p.Id, p.Name, Mean(byPeriod[p.Id])))
            .ToList();
    }

    /// <summary>
    /// The KPI rows grouped by the root node of the effective profiles of the positions present. Roots of different
    /// profiles with the same name are one group; a KPI under no root goes to a last group without a name.
    /// </summary>
    private static List<DepartmentKpiGroupDto> BuildKpiGroups(
        List<KpiRow> rows,
        Dictionary<Guid, decimal?> previousAchievement,
        ScoreSnapshot snapshot)
    {
        var titles = snapshot.Employees
            .GroupBy(e => e.JobPositionId)
            .ToDictionary(g => g.Key, g => g.First().JobPositionTitle);

        var groupOfKpi = new Dictionary<Guid, string>();
        var roots = new List<(string Name, int DisplayOrder, DepartmentGroupWeightDto Weight)>();
        foreach (var (positionId, positionRoots) in snapshot.PositionRoots.OrderBy(p => titles[p.Key], StringComparer.Ordinal))
        {
            foreach (var root in positionRoots)
            {
                var name = EvaluationProfileTree.NodeName(root);
                roots.Add((name, root.Item.DisplayOrder, new DepartmentGroupWeightDto(positionId, titles[positionId], root.Item.WeightPercent)));
                foreach (var leaf in EvaluationProfileTree.Leaves(root))
                {
                    groupOfKpi.TryAdd(leaf.Item.KpiDefinitionId!.Value, name);
                }
            }
        }

        var groups = roots
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .Select(g => new
            {
                Name = (string?)g.Key,
                DisplayOrder = g.Min(r => r.DisplayOrder),
                Weights = g.Select(r => r.Weight).OrderBy(w => w.JobPositionTitle, StringComparer.Ordinal).ToList(),
            })
            .OrderBy(g => g.DisplayOrder)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();

        var otherOrder = groups.Count == 0 ? 0 : groups.Max(g => g.DisplayOrder) + 1;
        groups.Add(new { Name = (string?)null, DisplayOrder = otherOrder, Weights = new List<DepartmentGroupWeightDto>() });

        var kpisByGroup = rows.ToLookup(r => groupOfKpi.GetValueOrDefault(r.KpiDefinitionId));
        return groups
            .Select(g => new DepartmentKpiGroupDto(
                g.Name,
                g.DisplayOrder,
                g.Weights,
                kpisByGroup[g.Name]
                    .OrderBy(r => r.Code, StringComparer.Ordinal)
                    .Select(r => new DepartmentGroupKpiDto(
                        r.KpiDefinitionId,
                        r.Code,
                        r.Name,
                        r.Unit,
                        r.Direction.ToEnumString(),
                        r.TargetValue,
                        r.ActualValue,
                        r.AchievementPercent,
                        previousAchievement.GetValueOrDefault(r.KpiDefinitionId),
                        Change(r.AchievementPercent, previousAchievement.GetValueOrDefault(r.KpiDefinitionId)),
                        r.CampaignActualValue))
                    .ToList()))
            .Where(g => g.Kpis.Count > 0)
            .ToList();
    }

    private List<DepartmentPositionDto> BuildPositions(ScoreSnapshot snapshot)
    {
        var weakScore = parameters.Value.PerfWeakGroupScore;
        return snapshot.Employees
            .GroupBy(e => e.JobPositionId)
            .Select(g =>
            {
                var members = g.ToList();
                var average = Mean(members.Where(e => snapshot.Current.ContainsKey(e.Id)).Select(e => snapshot.Current[e.Id].Score));
                var previousAverage = Mean(members.Where(e => snapshot.Previous.ContainsKey(e.Id)).Select(e => snapshot.Previous[e.Id]));
                var groups = snapshot.PositionRoots.GetValueOrDefault(g.Key, [])
                    .Select(root =>
                    {
                        var name = EvaluationProfileTree.NodeName(root);
                        var groupAverage = Mean(members
                            .Select(e => GroupScoreOf(snapshot, e.Id, name))
                            .Where(score => score.HasValue)
                            .Select(score => score!.Value));
                        return new DepartmentPositionGroupDto(
                            root.Item.Id,
                            name,
                            root.Item.DisplayOrder,
                            root.Item.WeightPercent,
                            groupAverage,
                            groupAverage < weakScore);
                    })
                    .ToList();

                return new DepartmentPositionDto(
                    g.Key,
                    members[0].JobPositionTitle,
                    members.Count,
                    members.Count(e => snapshot.Current.ContainsKey(e.Id)),
                    average,
                    previousAverage,
                    Change(average, previousAverage),
                    groups);
            })
            .OrderBy(p => p.JobPositionTitle, StringComparer.Ordinal)
            .ToList();
    }

    private List<DepartmentAttentionMemberDto> BuildAttention(
        ScoreSnapshot snapshot,
        Dictionary<Guid, int> overdueByEmployee)
    {
        var settings = parameters.Value;
        return snapshot.Employees
            .Where(e => snapshot.Current.ContainsKey(e.Id))
            .Select(e =>
            {
                var total = Round(snapshot.Current[e.Id].Score);
                decimal? previous = snapshot.Previous.TryGetValue(e.Id, out var p) ? Round(p) : null;
                var change = Change(total, previous);
                var overdue = overdueByEmployee.GetValueOrDefault(e.Id);

                var reasons = new List<string>();
                if (change <= -settings.PerfAttentionDropPoints)
                {
                    reasons.Add(AttentionReasons.ScoreDrop);
                }

                if (total < settings.PerfAttentionLowScore)
                {
                    reasons.Add(AttentionReasons.LowScore);
                }

                if (overdue > 0)
                {
                    reasons.Add(AttentionReasons.OverdueWork);
                }

                var weakest = snapshot.GroupScores.GetValueOrDefault(e.Id, [])
                    .Where(gs => gs.Score.HasValue)
                    .OrderBy(gs => gs.Score)
                    .Select(gs => new DepartmentWeakestGroupDto(gs.Name, gs.Score!.Value))
                    .FirstOrDefault();

                return new DepartmentAttentionMemberDto(
                    e.Id, e.Code, e.FullName, e.JobPositionTitle, total, previous, change, weakest, overdue, reasons);
            })
            .Where(m => m.Reasons.Count > 0)
            .OrderByDescending(m => m.Reasons.Count)
            .ThenBy(m => m.TotalScore)
            .ThenBy(m => m.EmployeeCode, StringComparer.Ordinal)
            .Take(Math.Max(settings.PerfAttentionTopN, 0))
            .ToList();
    }

    private static List<DepartmentChildUnitDto> BuildChildUnits(
        DepartmentHierarchy org,
        Guid departmentId,
        ScoreSnapshot snapshot) =>
        org.ChildrenOf(departmentId)
            .Select(id => org.Nodes[id])
            .Where(child => child.Status != RecordStatus.Archived)
            .OrderBy(child => child.Code, StringComparer.Ordinal)
            .Select(child =>
            {
                var subtree = org.Subtree(child.Id).ToHashSet();
                var members = snapshot.Employees.Where(e => subtree.Contains(e.DepartmentId)).ToList();
                var scored = members.Where(e => snapshot.Current.ContainsKey(e.Id)).ToList();
                var average = Mean(scored.Select(e => snapshot.Current[e.Id].Score));
                var previousAverage = Mean(members.Where(e => snapshot.Previous.ContainsKey(e.Id)).Select(e => snapshot.Previous[e.Id]));

                return new DepartmentChildUnitDto(
                    child.Id,
                    child.Code,
                    child.Name,
                    child.Level.ToEnumString(),
                    members.Count,
                    scored.Count,
                    average,
                    previousAverage,
                    Change(average, previousAverage));
            })
            .ToList();

    /// <summary>Employees scored in both periods, and those of them whose score dropped. Null without a compare period.</summary>
    private static (int? Declined, int? Compared) CountDeclined(ScoreSnapshot snapshot, bool hasComparePeriod)
    {
        if (!hasComparePeriod)
        {
            return (null, null);
        }

        var compared = snapshot.Current.Keys.Where(snapshot.Previous.ContainsKey).ToList();
        return (compared.Count(id => snapshot.Current[id].Score < snapshot.Previous[id]), compared.Count);
    }

    private static decimal? GroupScoreOf(ScoreSnapshot snapshot, Guid employeeId, string groupName) =>
        snapshot.GroupScores.GetValueOrDefault(employeeId, [])
            .FirstOrDefault(gs => string.Equals(gs.Name, groupName, StringComparison.Ordinal))?.Score;

    // ----- Statistics -----------------------------------------------------------------------

    private static decimal? RoundOrNull(decimal? value) => value.HasValue ? Round(value.Value) : null;

    private static decimal? Mean(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Round(list.Average());
    }

    private static decimal? Change(decimal? current, decimal? previous) =>
        current.HasValue && previous.HasValue ? Round(current.Value - previous.Value) : null;

    // ----- Private shapes -------------------------------------------------------------------

    private sealed record SnapshotEmployee(
        Guid Id,
        string Code,
        string FullName,
        Guid DepartmentId,
        Guid JobPositionId,
        string JobPositionTitle);

    private sealed record ScoredRow(Guid EvaluationId, Guid EmployeeId, Guid ProfileId, decimal Score);

    private sealed record GroupScore(string Name, decimal? Score);

    private sealed record ScoreSnapshot(
        List<SnapshotEmployee> Employees,
        Dictionary<Guid, ScoredRow> Current,
        Dictionary<Guid, decimal> Previous,
        Dictionary<Guid, IReadOnlyList<GroupScore>> GroupScores,
        Dictionary<Guid, IReadOnlyList<ProfileNode>> PositionRoots);
}
