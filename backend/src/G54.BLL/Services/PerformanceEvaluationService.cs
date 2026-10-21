using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Performance;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace G54.BLL.Services;

public sealed class PerformanceEvaluationService(
    AppDbContext db,
    IPermissionService permissions,
    IPerformanceScoreFormula formula,
    TimeProvider time,
    ILogger<PerformanceEvaluationService> logger) : IPerformanceEvaluationService
{
    private const string PermissionResource = "evaluation";
    private const string PermissionAction = "calculate";
    private const int DepartmentScopeLevel = 2;
    private const int BranchScopeLevel = 3;
    private const int AllScopeLevel = 4;
    private const int ScoreDecimals = 4;

    private static readonly Action<ILogger, Guid, Guid, int, int, Exception?> LogCalculated =
        LoggerMessage.Define<Guid, Guid, int, int>(
            LogLevel.Information,
            new EventId(1, "PerformanceCalculated"),
            "Performance calculated: EvaluationId={EvaluationId}, AccountId={AccountId}, ConvertedProgress={Converted}, SkippedResultItems={Skipped}");

    public async Task<PerformanceEvaluationDto> CalculateAsync(
        Guid accountId,
        Guid evaluationId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(accountId, cancellationToken);

        var evaluation = await db.PerformanceEvaluations
            .AsSplitQuery()
            .Include(e => e.Employee)
            .Include(e => e.EvaluationPeriod)
            .Include(e => e.EvaluationProfile)
                .ThenInclude(p => p.EvaluationProfileItems)
                    .ThenInclude(i => i.KpiGroup)
                        .ThenInclude(g => g.KpiDefinitions)
            .SingleOrDefaultAsync(e => e.Id == evaluationId, cancellationToken)
            ?? throw new NotFoundException("Đánh giá hiệu suất", evaluationId);

        await EnsureInScopeAsync(caller, evaluation.Employee.DepartmentId, cancellationToken);
        EnsureCalculable(evaluation);

        var now = time.GetUtcNow();

        var (newProgress, skipped) = await BuildNewProgressAsync(evaluation, now, cancellationToken);
        var progressRows = await LoadProgressRowsAsync(evaluation, cancellationToken);
        progressRows.AddRange(newProgress.Select(p => new ProgressRow(p.KpiDefinitionId, p.Value, p.Denominator)));

        var profileItems = evaluation.EvaluationProfile.EvaluationProfileItems
            .OrderBy(i => i.DisplayOrder)
            .ToList();
        var activeKpiIds = profileItems
            .SelectMany(i => i.KpiGroup.KpiDefinitions)
            .Where(k => k.Status == KpiStatus.Active)
            .Select(k => k.Id)
            .ToList();
        var targets = await LoadTargetsAsync(evaluation, activeKpiIds, cancellationToken);

        var existing = await db.PerformanceContributions
            .Where(c => c.PerformanceEvaluationId == evaluation.Id)
            .ToListAsync(cancellationToken);

        var keptIds = new HashSet<Guid>();
        var groupDtos = new List<PerformanceGroupContributionDto>();
        var groupEntries = new List<GroupScoreEntry>();

        foreach (var item in profileItems)
        {
            var group = existing.FirstOrDefault(c => c.EvaluationProfileItemId == item.Id);
            if (group is null)
            {
                group = NewContribution(evaluation, now);
                group.EvaluationProfileItemId = item.Id;
                existing.Add(group);
                db.PerformanceContributions.Add(group);
            }

            keptIds.Add(group.Id);
            group.WeightSnapshot = item.WeightPercent;
            group.UpdatedAt = now;

            var kpiEntries = new List<KpiScoreEntry>();
            var kpiDtos = new List<PerformanceKpiContributionDto>();
            var kpis = item.KpiGroup.KpiDefinitions
                .Where(k => k.Status == KpiStatus.Active)
                .OrderBy(k => k.Code, StringComparer.Ordinal);

            foreach (var kpi in kpis)
            {
                var target = ResolveTarget(targets, kpi.Id);
                var actual = Aggregate(kpi.AggregationType, progressRows.Where(r => r.KpiDefinitionId == kpi.Id).ToList());
                if (target is null || actual is null)
                {
                    continue;
                }

                var score = RoundScore(formula.CalculateKpi(new KpiScoreInput(
                    kpi.Id,
                    kpi.Code,
                    kpi.Unit,
                    kpi.Direction,
                    kpi.AggregationType,
                    kpi.MaxAchievementPercent,
                    target.TargetValue,
                    actual.Value,
                    actual.Denominator)));

                var contribution = existing.FirstOrDefault(c => c.KpiDefinitionId == kpi.Id);
                if (contribution is null)
                {
                    contribution = NewContribution(evaluation, now);
                    contribution.KpiDefinitionId = kpi.Id;
                    existing.Add(contribution);
                    db.PerformanceContributions.Add(contribution);
                }

                keptIds.Add(contribution.Id);
                contribution.ParentContributionId = group.Id;
                contribution.TargetAssignmentId = target.Id;
                contribution.TargetValueSnapshot = target.TargetValue;
                contribution.TargetVersionSnapshot = target.Version;
                contribution.ActualValue = actual.Value;
                contribution.ActualDenominator = actual.Denominator;
                contribution.AchievementPercentRaw = score.AchievementPercentRaw;
                contribution.AchievementPercentCapped = score.AchievementPercentCapped;
                contribution.ComponentScore = score.ComponentScore;
                contribution.UpdatedAt = now;

                kpiEntries.Add(new KpiScoreEntry(kpi.Id, kpi.Code, score));
                kpiDtos.Add(new PerformanceKpiContributionDto(
                    contribution.Id,
                    kpi.Id,
                    kpi.Code,
                    kpi.Name,
                    kpi.Unit,
                    target.Id,
                    target.TargetValue,
                    target.Version,
                    actual.Value,
                    actual.Denominator,
                    score.AchievementPercentRaw,
                    score.AchievementPercentCapped,
                    score.ComponentScore));
            }

            group.ComponentScore = RoundScore(formula.CalculateGroup(new GroupScoreInput(
                item.KpiGroupId,
                item.KpiGroup.Code,
                item.WeightPercent,
                kpiEntries)));

            groupEntries.Add(new GroupScoreEntry(item.KpiGroupId, item.WeightPercent, group.ComponentScore));
            groupDtos.Add(new PerformanceGroupContributionDto(
                group.Id,
                item.Id,
                item.KpiGroupId,
                item.KpiGroup.Code,
                item.KpiGroup.Name,
                item.WeightPercent,
                group.ComponentScore,
                kpiDtos));
        }

        // Children first so the parent link is never left dangling.
        foreach (var stale in existing.Where(c => !keptIds.Contains(c.Id))
                     .OrderByDescending(c => c.KpiDefinitionId.HasValue))
        {
            db.PerformanceContributions.Remove(stale);
        }

        // The stored score always reflects the latest calculation: a null total clears a stale one.
        var total = RoundScore(formula.CalculateTotal(groupEntries));
        evaluation.TotalScore = total;
        evaluation.CalculatedAt = total.HasValue ? now : null;
        evaluation.Status = total.HasValue ? PerformanceEvaluationStatus.Calculated : PerformanceEvaluationStatus.Draft;
        evaluation.UpdatedAt = now;
        db.ActualProgresses.AddRange(newProgress);

        await SaveAsync(cancellationToken);

        LogCalculated(logger, evaluation.Id, accountId, newProgress.Count, skipped, null);

        return new PerformanceEvaluationDto(
            evaluation.Id,
            evaluation.EmployeeId,
            evaluation.EvaluationPeriodId,
            evaluation.EvaluationProfileId,
            evaluation.TotalScore,
            evaluation.Status.ToEnumString(),
            evaluation.CalculatedAt,
            evaluation.UpdatedAt,
            groupDtos);
    }

    // ----- Authorization & state ------------------------------------------------------------

    private async Task<CallerScope> ResolveCallerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            throw new ForbiddenException();
        }

        var granted = await permissions.GetEffectivePermissionsAsync(accountId, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, PermissionResource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, PermissionAction, StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level < DepartmentScopeLevel)
        {
            throw new ForbiddenException();
        }

        return new CallerScope(account.DepartmentId, level);
    }

    private async Task EnsureInScopeAsync(
        CallerScope caller,
        Guid employeeDepartmentId,
        CancellationToken cancellationToken)
    {
        if (caller.Level >= AllScopeLevel)
        {
            return;
        }

        var nodes = await db.Departments.AsNoTracking()
            .Select(d => new DepartmentNode(d.Id, d.ParentDepartmentId, d.LevelType))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        if (!nodes.ContainsKey(caller.DepartmentId))
        {
            throw new ForbiddenException();
        }

        var rootId = caller.Level >= BranchScopeLevel ? BranchRoot(nodes, caller.DepartmentId) : caller.DepartmentId;
        if (!IsWithin(nodes, employeeDepartmentId, rootId))
        {
            throw new ForbiddenException("Bạn không có quyền tính điểm KPI cho nhân viên này.");
        }
    }

    private static void EnsureCalculable(PerformanceEvaluation evaluation)
    {
        if (evaluation.Status is not (PerformanceEvaluationStatus.Draft or PerformanceEvaluationStatus.Calculated))
        {
            throw new ConflictException(
                $"Đánh giá đang ở trạng thái {evaluation.Status.ToEnumString()}, không thể tính lại điểm KPI.",
                ErrorCodes.PerformanceEvaluationInvalidState);
        }

        if (evaluation.EvaluationPeriod.Status is PeriodStatus.Locked or PeriodStatus.Closed)
        {
            throw new ConflictException(
                $"Kỳ đánh giá đang ở trạng thái {evaluation.EvaluationPeriod.Status.ToEnumString()}, không thể tính điểm KPI.",
                ErrorCodes.PerformanceEvaluationInvalidState);
        }
    }

    /// <summary>Nearest BRANCH ancestor-or-self; the root-most ancestor when none exists.</summary>
    private static Guid BranchRoot(Dictionary<Guid, DepartmentNode> nodes, Guid departmentId)
    {
        var current = departmentId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            var node = nodes[current];
            if (node.Level == DepartmentLevelType.Branch
                || !node.ParentId.HasValue
                || !nodes.ContainsKey(node.ParentId.Value))
            {
                return current;
            }

            current = node.ParentId.Value;
        }

        return current;
    }

    /// <summary>True when the department is the root or one of its descendants (cycle-safe).</summary>
    private static bool IsWithin(Dictionary<Guid, DepartmentNode> nodes, Guid departmentId, Guid rootId)
    {
        Guid? current = departmentId;
        var seen = new HashSet<Guid>();
        while (current.HasValue && seen.Add(current.Value))
        {
            if (current.Value == rootId)
            {
                return true;
            }

            current = nodes.TryGetValue(current.Value, out var node) ? node.ParentId : null;
        }

        return false;
    }

    // ----- Verified results -> actual progress ----------------------------------------------

    /// <summary>
    /// Converts approved, still valid work result items of the evaluated employee into actual progress.
    /// Items that already have progress are skipped, so a result contributes at most once.
    /// </summary>
    private async Task<(List<ActualProgress> Rows, int Skipped)> BuildNewProgressAsync(
        PerformanceEvaluation evaluation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var period = evaluation.EvaluationPeriod;
        var from = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var items = await db.WorkResultItems.AsNoTracking()
            .Where(i => i.VerifiedWorkResult.Status == VerifiedResultStatus.Valid
                && i.VerifiedWorkResult.Verification.Decision == VerificationDecision.Approved
                && i.VerifiedWorkResult.Verification.VerifiedAt != null
                && i.VerifiedWorkResult.Verification.VerifiedAt >= from
                && i.VerifiedWorkResult.Verification.VerifiedAt < to
                && i.VerifiedWorkResult.Verification.ResultSubmission.SubmitterEmployeeId == evaluation.EmployeeId
                && !db.ActualProgresses.Any(a => a.SourceItemId == i.Id))
            .Select(i => new SourceItem(
                i.Id,
                i.KpiDefinitionId,
                i.KpiDefinition.AggregationType,
                i.Value,
                i.Denominator,
                i.VerifiedWorkResult.Verification.VerifiedAt!.Value))
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return ([], 0);
        }

        var dayPeriods = new Dictionary<DateOnly, Guid>();
        var days = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType == PeriodType.Day
                && p.StartDate >= period.StartDate
                && p.EndDate <= period.EndDate)
            .Select(p => new { p.Id, p.StartDate, p.EndDate })
            .ToListAsync(cancellationToken);
        foreach (var day in days.Where(d => d.StartDate == d.EndDate))
        {
            dayPeriods.TryAdd(day.StartDate, day.Id);
        }

        var rows = new List<ActualProgress>();
        var skipped = 0;
        foreach (var item in items)
        {
            var progressDate = DateOnly.FromDateTime(item.VerifiedAt.UtcDateTime);
            if (!IsConvertible(item) || !dayPeriods.TryGetValue(progressDate, out var dayPeriodId))
            {
                skipped++;
                continue;
            }

            rows.Add(new ActualProgress
            {
                Id = Guid.NewGuid(),
                KpiDefinitionId = item.KpiDefinitionId,
                EmployeeId = evaluation.EmployeeId,
                EvaluationPeriodId = dayPeriodId,
                ProgressDate = progressDate,
                Value = item.Value,
                Denominator = item.Denominator,
                SourceItemId = item.Id,
                Status = ActualProgressStatus.Valid,
                UpdatedAt = now,
            });
        }

        return (rows, skipped);
    }

    private static bool IsConvertible(SourceItem item)
    {
        if (item.Value < 0)
        {
            return false;
        }

        return item.AggregationType == KpiAggregationType.Ratio
            ? item.Denominator is > 0
            : item.Denominator is null;
    }

    private async Task<List<ProgressRow>> LoadProgressRowsAsync(
        PerformanceEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var period = evaluation.EvaluationPeriod;
        return await db.ActualProgresses.AsNoTracking()
            .Where(a => a.EmployeeId == evaluation.EmployeeId
                && a.Status == ActualProgressStatus.Valid
                && a.ProgressDate >= period.StartDate
                && a.ProgressDate <= period.EndDate)
            .Select(a => new ProgressRow(a.KpiDefinitionId, a.Value, a.Denominator))
            .ToListAsync(cancellationToken);
    }

    // ----- Targets & aggregation ------------------------------------------------------------

    private async Task<List<TargetRow>> LoadTargetsAsync(
        PerformanceEvaluation evaluation,
        List<Guid> kpiIds,
        CancellationToken cancellationToken)
    {
        var departmentId = evaluation.Employee.DepartmentId;
        return await db.TargetAssignments.AsNoTracking()
            .Where(t => t.EvaluationPeriodId == evaluation.EvaluationPeriodId
                && t.Status == TargetAssignmentStatus.Active
                && kpiIds.Contains(t.KpiDefinitionId)
                && (t.EmployeeId == evaluation.EmployeeId
                    || (t.EmployeeId == null && t.DepartmentId == departmentId)))
            .Select(t => new TargetRow(t.Id, t.KpiDefinitionId, t.EmployeeId != null, t.Version, t.TargetValue))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Employee-level targets win over department-level ones; the highest version wins within a level.</summary>
    private static TargetRow? ResolveTarget(List<TargetRow> targets, Guid kpiId) =>
        targets
            .Where(t => t.KpiDefinitionId == kpiId)
            .OrderByDescending(t => t.IsEmployeeLevel)
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

    // ----- Persistence ----------------------------------------------------------------------

    private static PerformanceContribution NewContribution(PerformanceEvaluation evaluation, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        PerformanceEvaluationId = evaluation.Id,
        EmployeeId = evaluation.EmployeeId,
        EvaluationPeriodId = evaluation.EvaluationPeriodId,
        UpdatedAt = now,
    };

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Điểm KPI vừa được cập nhật bởi một yêu cầu khác. Vui lòng thử lại.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Điểm KPI vừa được cập nhật bởi một yêu cầu khác. Vui lòng thử lại.");
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

    private sealed record CallerScope(Guid DepartmentId, int Level);

    private sealed record DepartmentNode(Guid Id, Guid? ParentId, DepartmentLevelType Level);

    private sealed record SourceItem(
        Guid Id,
        Guid KpiDefinitionId,
        KpiAggregationType AggregationType,
        decimal Value,
        decimal? Denominator,
        DateTimeOffset VerifiedAt);

    private sealed record ProgressRow(Guid KpiDefinitionId, decimal Value, decimal? Denominator);

    private sealed record TargetRow(Guid Id, Guid KpiDefinitionId, bool IsEmployeeLevel, int Version, decimal TargetValue);

    private sealed record ActualAggregate(decimal Value, decimal? Denominator);
}
