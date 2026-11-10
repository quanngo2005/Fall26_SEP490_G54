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
                appealNet.GetValueOrDefault(subject.EmployeeId),
                period);
        }

        return results;
    }

    private PerformanceScoreResult ScoreSubject(
        PerformanceScoreSubject subject,
        IReadOnlyList<ProfileNode> roots,
        List<TargetRow> targets,
        List<ProgressRow> progress,
        List<RiskEvent> risks,
        decimal appealNet,
        EvaluationPeriod period)
    {
        // Every version of a KPI in effect during the period is its own segment (BR-15): one ScoredKpi per segment,
        // its target and weight prorated to the days it covers. A KPI with a single segment spanning the whole
        // period (the common case, and every case before BR-15) scores exactly as before.
        var segmentsByKpi = new Dictionary<Guid, List<ScoredKpi>>();
        foreach (var leaf in roots.SelectMany(EvaluationProfileTree.ActiveLeaves))
        {
            var kpi = leaf.Kpi!;
            var kpiProgress = progress.Where(row => row.KpiDefinitionId == kpi.Id).ToList();
            var targetRows = ResolveTargets(targets, subject, kpi.Id);
            segmentsByKpi[kpi.Id] = ScoreKpi(leaf, kpi, targetRows, kpiProgress, period);
        }

        // The KPI's score fed into the profile tree is the sum of its segments' scores: each segment's target and
        // actual already cover only the days it was in effect, so the segments add up to the period total.
        decimal? AggregateKpiScore(Guid kpiId)
        {
            var segments = segmentsByKpi[kpiId];
            return segments.Any(s => s.ComponentScore.HasValue)
                ? segments.Sum(s => s.ComponentScore ?? 0m)
                : null;
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
                    ? AggregateKpiScore(kpi.Id)
                    : null);
            }

            var shares = EvaluationProfileTree.LeafShares(root);
            var kpis = EvaluationProfileTree.ActiveLeaves(root)
                .SelectMany(leaf =>
                {
                    var kpiId = leaf.Item.KpiDefinitionId!.Value;
                    var share = shares.GetValueOrDefault(kpiId);
                    return segmentsByKpi[kpiId].Select(segment =>
                    {
                        // The segment's share of the root follows the same day-fraction the segment's weight does.
                        var fraction = leaf.Item.WeightPercent > 0m ? segment.ItemWeightPercent / leaf.Item.WeightPercent : 1m;
                        return segment with { SharePercent = share * fraction };
                    });
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
        var penalty = risks.Sum(EvaluationService.RiskPenalty);
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

    /// <summary>
    /// One <see cref="ScoredKpi"/> per version of the KPI's target in effect during <paramref name="period"/> (BR-15
    /// segments), ordered by effective date. A version's target value and the leaf's weight are prorated to the
    /// fraction of the period it covers; its actual is aggregated only from the progress whose
    /// <see cref="ProgressRow.ProgressDate"/> falls in that version's <c>[EffectiveFrom, EffectiveTo ?? period end]</c>
    /// window (segments partition the period, so every date matches exactly one). Matching by date rather than by
    /// the exact <see cref="ProgressRow.TargetAssignmentId"/> it was recorded against also keeps counting progress
    /// attached to an older carrier row (e.g. after a plain reallocation) towards whichever segment covers its date.
    /// No target at all still yields one row (actual only, no score), as before BR-15.
    /// </summary>
    private List<ScoredKpi> ScoreKpi(ProfileNode leaf, KpiDefinition kpi, List<TargetRow> targetRows, List<ProgressRow> rows, EvaluationPeriod period)
    {
        if (targetRows.Count == 0)
        {
            return [ScoreSegment(leaf, kpi, null, rows)];
        }

        var periodDays = period.EndDate.DayNumber - period.StartDate.DayNumber + 1;
        return targetRows
            .OrderBy(t => t.EffectiveFrom)
            .ThenBy(t => t.Version)
            .Select(target =>
            {
                var (start, end) = SegmentWindow(target, period);
                // Progress is matched by its date falling in the segment's window, not by which exact
                // TargetAssignment row it was recorded against: a result can be attached to an older carrier row
                // (handover reconciliation, a plain reallocation) that never resolves as a scoring segment itself,
                // and must still count towards whichever segment is in effect on its date.
                var segmentRows = rows.Where(r => r.ProgressDate >= start && r.ProgressDate <= end).ToList();
                var fraction = periodDays > 0 && end >= start ? (decimal)(end.DayNumber - start.DayNumber + 1) / periodDays : 1m;
                return ScoreSegment(leaf, kpi, (target, fraction), segmentRows);
            })
            .ToList();
    }

    /// <summary>A version's <c>[EffectiveFrom, EffectiveTo ?? period end]</c> window, clamped to the period.</summary>
    private static (DateOnly Start, DateOnly End) SegmentWindow(TargetRow target, EvaluationPeriod period)
    {
        var start = target.EffectiveFrom > period.StartDate ? target.EffectiveFrom : period.StartDate;
        var endRaw = target.EffectiveTo ?? period.EndDate;
        var end = endRaw < period.EndDate ? endRaw : period.EndDate;
        return (start, end);
    }

    private ScoredKpi ScoreSegment(ProfileNode leaf, KpiDefinition kpi, (TargetRow Target, decimal Fraction)? segment, List<ProgressRow> rows)
    {
        var actual = Aggregate(kpi.AggregationType, rows);

        // The product multiplier (BR-48) converts the real amount to points only here; other aggregations score the real actual.
        var weightedActual = actual is not null && kpi.AggregationType == KpiAggregationType.Sum
            ? rows.Sum(r => r.WeightedValue)
            : actual?.Value;
        // Rounded to the column's own precision (numeric(18,4)): a day-fraction multiplication otherwise leaves
        // decimal residue (e.g. 310m * 15m / 31m) that a single-segment target (fraction == 1) never had.
        var targetValue = segment is { } s ? Math.Round(s.Target.TargetValue * s.Fraction, 4, MidpointRounding.ToEven) : (decimal?)null;
        var score = targetValue is not null && actual is not null
            ? RoundScore(formula.CalculateKpi(new KpiScoreInput(
                kpi.Id,
                kpi.Code,
                kpi.Unit,
                kpi.Direction,
                kpi.AggregationType,
                kpi.MaxAchievementPercent,
                targetValue.Value,
                weightedActual!.Value,
                actual.Denominator)))
            : null;

        // The KPI is identified through its progress; the latest row anchors the aggregate.
        var anchor = rows
            .OrderByDescending(r => r.ProgressDate)
            .ThenByDescending(r => r.ProgressId)
            .FirstOrDefault();
        var weightShare = segment is { } sg ? leaf.Item.WeightPercent * sg.Fraction : leaf.Item.WeightPercent;

        return new ScoredKpi(
            kpi.Id,
            kpi.Code,
            kpi.Name,
            kpi.Unit,
            weightShare,
            0m,
            segment?.Target.Id,
            targetValue,
            segment?.Target.Version,
            actual?.Value,
            weightedActual,
            actual?.Denominator,
            anchor?.ProgressId,
            score?.AchievementPercentRaw,
            score?.AchievementPercentCapped,
            score?.ComponentScore);
    }

    // ----- Data (one query per table for all subjects) --------------------------------------

    /// <summary>Every version of a target in effect at some point in the period: ACTIVE, and SUPERSEDED versions that
    /// carry a real BR-15 effective-dated window (<c>EffectiveTo != null</c>). A SUPERSEDED row with no
    /// <c>EffectiveTo</c> was replaced before BR-15 (or by a plain reallocation) and is not a scoring segment.</summary>
    private async Task<List<TargetRow>> LoadTargetsAsync(
        EvaluationPeriod period,
        List<Guid> employeeIds,
        List<Guid> departmentIds,
        List<Guid> kpiIds,
        CancellationToken cancellationToken) =>
        await db.TargetAssignments.AsNoTracking()
            .Where(t => t.EvaluationPeriodId == period.Id
                && kpiIds.Contains(t.KpiDefinitionId)
                && ((t.EmployeeId != null && employeeIds.Contains(t.EmployeeId.Value))
                    || (t.EmployeeId == null && t.DepartmentId != null && departmentIds.Contains(t.DepartmentId.Value)))
                && (t.Status == TargetAssignmentStatus.Active
                    || (t.Status == TargetAssignmentStatus.Superseded && t.EffectiveTo != null)))
            .Select(t => new TargetRow(t.Id, t.KpiDefinitionId, t.EmployeeId, t.DepartmentId, t.Version, t.TargetValue, t.EffectiveFrom, t.EffectiveTo))
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
                a.TargetAssignmentId,
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
                r.TargetAssignmentId,
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

    /// <summary>
    /// Employee-level targets win over department-level ones; every version at the winning level is returned as a
    /// segment, except the still-open (ACTIVE, <c>EffectiveTo == null</c>) row: the database allows only one such row
    /// per KPI/period/owner, so more than one here is bad data, and the highest version wins (same precedence as
    /// before BR-15) rather than becoming two overlapping segments.
    /// </summary>
    private static List<TargetRow> ResolveTargets(List<TargetRow> targets, PerformanceScoreSubject subject, Guid kpiId)
    {
        var candidates = targets
            .Where(t => t.KpiDefinitionId == kpiId
                && (t.EmployeeId == subject.EmployeeId
                    || (t.EmployeeId is null && t.DepartmentId == subject.DepartmentId)))
            .ToList();
        var employeeLevel = candidates.Where(t => t.EmployeeId.HasValue).ToList();
        var winners = employeeLevel.Count > 0 ? employeeLevel : candidates;

        var closedSegments = winners.Where(t => t.EffectiveTo is not null).ToList();
        var openEnded = winners.Where(t => t.EffectiveTo is null).ToList();
        if (openEnded.Count > 1)
        {
            openEnded = [openEnded.OrderByDescending(t => t.Version).First()];
        }

        return [.. closedSegments, .. openEnded];
    }

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
                // A day row stores the sum of its entries and their count; a row without a count is one entry.
                return new ActualAggregate(
                    Math.Round(rows.Sum(r => r.Value) / rows.Sum(r => r.Denominator ?? 1m), ScoreDecimals, MidpointRounding.ToEven),
                    null);
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
        decimal TargetValue,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo);

    private sealed record ProgressRow(
        Guid ProgressId,
        Guid EmployeeId,
        Guid KpiDefinitionId,
        Guid TargetAssignmentId,
        DateOnly ProgressDate,
        decimal Value,
        decimal WeightedValue,
        decimal? Denominator);

    private sealed record ActualAggregate(decimal Value, decimal? Denominator);
}
