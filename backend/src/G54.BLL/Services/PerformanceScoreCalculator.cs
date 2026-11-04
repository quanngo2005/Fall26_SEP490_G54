using G54.BLL.Dtos.Evaluation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class PerformanceScoreCalculator(AppDbContext db, IPerformanceScoreFormula formula) : IPerformanceScoreCalculator
{
    /// <summary>Decimal places of the score columns (numeric(10,4)); every score is rounded to it once, here.</summary>
    internal const int ScoreDecimals = 4;

    public async Task<IReadOnlyDictionary<Guid, PerformanceScoreResult>> CalculateAsync(
        IReadOnlyCollection<PerformanceScoreSubject> subjects,
        EvaluationPeriod period,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<Guid, PerformanceScoreResult>();
        if (subjects.Count == 0)
        {
            return results;
        }

        var roots = subjects.ToDictionary(
            s => s.EmployeeId,
            s => EvaluationProfileTree.Build(s.Profile.EvaluationProfileItems));
        var employeeIds = roots.Keys.ToList();
        var departmentIds = subjects.Select(s => s.DepartmentId).Distinct().ToList();
        var kpiIds = roots.Values
            .SelectMany(r => r)
            .SelectMany(EvaluationProfileTree.ActiveLeaves)
            .Select(leaf => leaf.Item.KpiDefinitionId!.Value)
            .Distinct()
            .ToList();

        var targets = await LoadTargetsAsync(period, employeeIds, departmentIds, kpiIds, cancellationToken);
        var progress = (await LoadProgressAsync(period, employeeIds, cancellationToken))
            .ToLookup(row => row.EmployeeId);
        var risks = (await LoadRiskEventsAsync(period, employeeIds, cancellationToken))
            .ToLookup(risk => risk.EmployeeId);
        var appealNet = await AppealScoreLedger.NetByEmployeeAsync(db, employeeIds, period.Id, cancellationToken);

        foreach (var subject in subjects)
        {
            results[subject.EmployeeId] = ScoreSubject(
                subject,
                roots[subject.EmployeeId],
                targets,
                progress[subject.EmployeeId].ToList(),
                risks[subject.EmployeeId].OrderByDescending(r => r.OccurredAt).ToList(),
                appealNet.GetValueOrDefault(subject.EmployeeId));
        }

        return results;
    }

    private PerformanceScoreResult ScoreSubject(
        PerformanceScoreSubject subject,
        IReadOnlyList<ProfileNode> roots,
        List<TargetRow> targets,
        List<ProgressRow> progress,
        List<RiskEvent> risks,
        decimal appealNet)
    {
        var scoredKpis = new Dictionary<Guid, ScoredKpi>();
        foreach (var leaf in roots.SelectMany(EvaluationProfileTree.ActiveLeaves))
        {
            var kpi = leaf.Kpi!;
            scoredKpis[kpi.Id] = ScoreKpi(
                leaf,
                kpi,
                ResolveTarget(targets, subject, kpi.Id),
                progress.Where(row => row.KpiDefinitionId == kpi.Id).ToList());
        }

        var groups = new List<ScoredGroup>();
        foreach (var root in roots)
        {
            var isQualitative = EvaluationProfileTree.IsQualitative(root);
            QualitativeScoreInputDto? qualitative = null;
            decimal? score;
            if (isQualitative)
            {
                // A group the reviewer has not scored yet counts as 0, never as a full score (awaiting BA confirmation).
                subject.QualitativeScores.TryGetValue(root.Item.Id, out qualitative);
                score = RoundScore(qualitative?.Score ?? 0m);
            }
            else
            {
                score = ScoreNode(root, leaf => leaf.Kpi is { Status: KpiStatus.Active } kpi
                    ? scoredKpis[kpi.Id].ComponentScore
                    : null);
            }

            var shares = EvaluationProfileTree.LeafShares(root);
            var kpis = EvaluationProfileTree.ActiveLeaves(root)
                .Select(leaf => scoredKpis[leaf.Item.KpiDefinitionId!.Value] with
                {
                    SharePercent = shares.GetValueOrDefault(leaf.Item.KpiDefinitionId!.Value),
                })
                .ToList();

            groups.Add(new ScoredGroup(
                root.Item.Id,
                EvaluationProfileTree.NodeName(root),
                EvaluationProfileTree.CatalogGroupCode(root),
                root.Item.WeightPercent,
                isQualitative,
                qualitative?.Score,
                qualitative?.Notes,
                score,
                kpis));
        }

        var weighted = RoundScore(formula.CalculateTotal(
            groups.Select(g => new GroupScoreEntry(g.EvaluationProfileItemId, g.WeightPercent, g.Score)).ToList()));
        var penalty = risks.Sum(risk => EvaluationService.MapRiskPenalty(risk.Severity));
        decimal? total = weighted.HasValue
            ? RoundScore(Math.Max(0m, weighted.Value - penalty) + appealNet)
            : null;

        var quantitative = RoundScore(EvaluationProfileTree.WeightedMean(
            groups.Where(g => !g.IsQualitative).Select(g => (g.WeightPercent, g.Score))));

        return new PerformanceScoreResult(subject.EmployeeId, groups, weighted, risks, penalty, total, quantitative);
    }

    /// <summary>
    /// Scores a node of the profile tree: a KPI leaf through <paramref name="leafScore"/> and
    /// <see cref="EvaluationProfileTree.ScoredLeaf"/> (an active KPI that cannot be scored counts as 0), a group through
    /// the formula from its children weighted among siblings. Only an inactive KPI is left out and the others normalised.
    /// </summary>
    private decimal? ScoreNode(ProfileNode node, Func<ProfileNode, decimal?> leafScore)
    {
        if (node.IsLeaf)
        {
            return EvaluationProfileTree.ScoredLeaf(node, leafScore(node));
        }

        var parts = node.Children
            .Select(child => new GroupScorePart(child.Item.WeightPercent, ScoreNode(child, leafScore)))
            .ToList();

        return RoundScore(formula.CalculateGroup(new GroupScoreInput(
            node.Item.Id,
            EvaluationProfileTree.NodeName(node),
            node.Item.WeightPercent,
            parts)));
    }

    private ScoredKpi ScoreKpi(ProfileNode leaf, KpiDefinition kpi, TargetRow? target, List<ProgressRow> rows)
    {
        var actual = Aggregate(kpi.AggregationType, rows);

        // The product multiplier (BR-48) converts the real amount to points only here; other aggregations score the real actual.
        var weightedActual = actual is not null && kpi.AggregationType == KpiAggregationType.Sum
            ? rows.Sum(r => r.WeightedValue)
            : actual?.Value;
        var score = target is not null && actual is not null
            ? RoundScore(formula.CalculateKpi(new KpiScoreInput(
                kpi.Id,
                kpi.Code,
                kpi.Unit,
                kpi.Direction,
                kpi.AggregationType,
                kpi.MaxAchievementPercent,
                target.TargetValue,
                weightedActual!.Value,
                actual.Denominator)))
            : null;

        // The KPI is identified through its progress; the latest row anchors the aggregate.
        var anchor = rows
            .OrderByDescending(r => r.ProgressDate)
            .ThenByDescending(r => r.ProgressId)
            .FirstOrDefault();

        return new ScoredKpi(
            kpi.Id,
            kpi.Code,
            kpi.Name,
            kpi.Unit,
            leaf.Item.WeightPercent,
            0m,
            target?.Id,
            target?.TargetValue,
            target?.Version,
            actual?.Value,
            weightedActual,
            actual?.Denominator,
            anchor?.ProgressId,
            score?.AchievementPercentRaw,
            score?.AchievementPercentCapped,
            score?.ComponentScore);
    }

    // ----- Data (one query per table for all subjects) --------------------------------------

    private async Task<List<TargetRow>> LoadTargetsAsync(
        EvaluationPeriod period,
        List<Guid> employeeIds,
        List<Guid> departmentIds,
        List<Guid> kpiIds,
        CancellationToken cancellationToken) =>
        await db.TargetAssignments.AsNoTracking()
            .Where(t => t.EvaluationPeriodId == period.Id
                && t.Status == TargetAssignmentStatus.Active
                && kpiIds.Contains(t.KpiDefinitionId)
                && ((t.EmployeeId != null && employeeIds.Contains(t.EmployeeId.Value))
                    || (t.EmployeeId == null && t.DepartmentId != null && departmentIds.Contains(t.DepartmentId.Value))))
            .Select(t => new TargetRow(t.Id, t.KpiDefinitionId, t.EmployeeId, t.DepartmentId, t.Version, t.TargetValue))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Valid progress recorded against each employee's own targets within the period. Progress rows are written when
    /// results are verified, so scoring only reads them.
    /// </summary>
    private async Task<List<ProgressRow>> LoadProgressAsync(
        EvaluationPeriod period,
        List<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.ActualProgresses.AsNoTracking()
            .Where(a => a.TargetAssignment.EmployeeId != null
                && employeeIds.Contains(a.TargetAssignment.EmployeeId.Value)
                && a.Status == ActualProgressStatus.Valid
                && a.ProgressDate >= period.StartDate
                && a.ProgressDate <= period.EndDate)
            .Select(a => new
            {
                a.Id,
                EmployeeId = a.TargetAssignment.EmployeeId!.Value,
                a.TargetAssignment.KpiDefinitionId,
                a.ProgressDate,
                a.Value,
                a.Denominator,
            })
            .ToListAsync(cancellationToken);

        // Points = the real amount of each verified result times the multiplier it was approved with.
        var progressIds = rows.Select(r => r.Id).ToList();
        var weightedByProgress = (await db.WorkResultItems.AsNoTracking()
                .Where(i => progressIds.Contains(i.ActualProgressId)
                    && i.VerifiedWorkResult.Status == VerifiedResultStatus.Valid)
                .Select(i => new { i.ActualProgressId, i.Value })
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.ActualProgressId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Value));

        return rows
            .Select(r => new ProgressRow(
                r.Id,
                r.EmployeeId,
                r.KpiDefinitionId,
                r.ProgressDate,
                r.Value,
                weightedByProgress.GetValueOrDefault(r.Id, r.Value),
                r.Denominator))
            .ToList();
    }

    /// <summary>Risk events that happened inside the period and were not dismissed.</summary>
    private async Task<List<RiskEvent>> LoadRiskEventsAsync(
        EvaluationPeriod period,
        List<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        return await db.RiskEvents.AsNoTracking()
            .Where(re => employeeIds.Contains(re.EmployeeId)
                && re.OccurredAt >= periodStart
                && re.OccurredAt <= periodEnd
                && re.Status != RiskEventStatus.Dismissed)
            .ToListAsync(cancellationToken);
    }

    // ----- Targets & aggregation ------------------------------------------------------------

    /// <summary>Employee-level targets win over department-level ones; the highest version wins within a level.</summary>
    private static TargetRow? ResolveTarget(List<TargetRow> targets, PerformanceScoreSubject subject, Guid kpiId) =>
        targets
            .Where(t => t.KpiDefinitionId == kpiId
                && (t.EmployeeId == subject.EmployeeId
                    || (t.EmployeeId is null && t.DepartmentId == subject.DepartmentId)))
            .OrderByDescending(t => t.EmployeeId.HasValue)
            .ThenByDescending(t => t.Version)
            .FirstOrDefault();

    private static ActualAggregate? Aggregate(KpiAggregationType type, List<ProgressRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        switch (type)
        {
            case KpiAggregationType.Sum:
                return new ActualAggregate(rows.Sum(r => r.Value), null);
            case KpiAggregationType.Avg:
                return new ActualAggregate(Math.Round(rows.Average(r => r.Value), ScoreDecimals, MidpointRounding.ToEven), null);
            case KpiAggregationType.Ratio:
                var denominator = rows.Sum(r => r.Denominator ?? 0m);
                return denominator > 0m ? new ActualAggregate(rows.Sum(r => r.Value), denominator) : null;
            default:
                return null;
        }
    }

    // ----- Scores ---------------------------------------------------------------------------

    private static decimal? RoundScore(decimal? value) =>
        value.HasValue ? Math.Round(value.Value, ScoreDecimals, MidpointRounding.ToEven) : null;

    private static KpiScoreResult RoundScore(KpiScoreResult result) => new(
        RoundScore(result.AchievementPercentRaw),
        RoundScore(result.AchievementPercentCapped),
        RoundScore(result.ComponentScore));

    // ----- Private shapes -------------------------------------------------------------------

    private sealed record TargetRow(
        Guid Id,
        Guid KpiDefinitionId,
        Guid? EmployeeId,
        Guid? DepartmentId,
        int Version,
        decimal TargetValue);

    private sealed record ProgressRow(
        Guid ProgressId,
        Guid EmployeeId,
        Guid KpiDefinitionId,
        DateOnly ProgressDate,
        decimal Value,
        decimal WeightedValue,
        decimal? Denominator);

    private sealed record ActualAggregate(decimal Value, decimal? Denominator);
}
