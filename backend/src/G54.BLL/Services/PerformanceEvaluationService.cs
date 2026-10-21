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

    private static readonly Action<ILogger, Guid, Guid, int, Exception?> LogCalculated =
        LoggerMessage.Define<Guid, Guid, int>(
            LogLevel.Information,
            new EventId(1, "PerformanceCalculated"),
            "Performance calculated: EvaluationId={EvaluationId}, AccountId={AccountId}, ProgressRows={ProgressRows}");

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

        var progressRows = await LoadProgressRowsAsync(evaluation, cancellationToken);

        var profileItems = evaluation.EvaluationProfile.EvaluationProfileItems
            .OrderBy(i => i.DisplayOrder)
            .ToList();
        var activeKpiIds = profileItems
            .SelectMany(i => i.KpiGroup.KpiDefinitions)
            .Where(k => k.Status == KpiStatus.Active)
            .Select(k => k.Id)
            .ToList();
        var targets = await LoadTargetsAsync(evaluation, activeKpiIds, cancellationToken);

        // Risk-event contributions are owned elsewhere; only the KPI rows this calculation writes are managed here.
        var existing = await db.PerformanceContributions
            .Include(c => c.ActualProgress)
                .ThenInclude(a => a!.TargetAssignment)
            .Where(c => c.PerformanceEvaluationId == evaluation.Id && c.RiskEventId == null)
            .ToListAsync(cancellationToken);

        var keptIds = new HashSet<Guid>();
        var groupDtos = new List<PerformanceGroupContributionDto>();
        var groupEntries = new List<GroupScoreEntry>();

        foreach (var item in profileItems)
        {
            var kpiEntries = new List<KpiScoreEntry>();
            var kpiDtos = new List<PerformanceKpiContributionDto>();
            var kpis = item.KpiGroup.KpiDefinitions
                .Where(k => k.Status == KpiStatus.Active)
                .OrderBy(k => k.Code, StringComparer.Ordinal);

            foreach (var kpi in kpis)
            {
                var target = ResolveTarget(targets, kpi.Id);
                var kpiRows = progressRows.Where(r => r.KpiDefinitionId == kpi.Id).ToList();
                var actual = Aggregate(kpi.AggregationType, kpiRows);
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

                var contribution = existing.FirstOrDefault(c =>
                    !keptIds.Contains(c.Id) && c.ActualProgress?.TargetAssignment.KpiDefinitionId == kpi.Id);
                if (contribution is null)
                {
                    contribution = NewContribution(evaluation, now);
                    existing.Add(contribution);
                    db.PerformanceContributions.Add(contribution);
                }

                // The KPI is identified through its progress; the latest row anchors the aggregate.
                keptIds.Add(contribution.Id);
                contribution.ActualProgressId = kpiRows
                    .OrderByDescending(r => r.ProgressDate)
                    .ThenByDescending(r => r.ProgressId)
                    .First()
                    .ProgressId;
                contribution.WeightSnapshot = item.WeightPercent;
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

            // Group scores are not persisted: the schema stores contributions per KPI only.
            var groupScore = RoundScore(formula.CalculateGroup(new GroupScoreInput(
                item.KpiGroupId,
                item.KpiGroup.Code,
                item.WeightPercent,
                kpiEntries)));

            groupEntries.Add(new GroupScoreEntry(item.KpiGroupId, item.WeightPercent, groupScore));
            groupDtos.Add(new PerformanceGroupContributionDto(
                item.Id,
                item.KpiGroupId,
                item.KpiGroup.Code,
                item.KpiGroup.Name,
                item.WeightPercent,
                groupScore,
                kpiDtos));
        }

        foreach (var stale in existing.Where(c => !keptIds.Contains(c.Id)))
        {
            db.PerformanceContributions.Remove(stale);
        }

        // The stored score always reflects the latest calculation: a null total clears a stale one.
        var total = RoundScore(formula.CalculateTotal(groupEntries));
        evaluation.TotalScore = total;
        evaluation.CalculatedAt = total.HasValue ? now : null;
        evaluation.Status = total.HasValue ? PerformanceEvaluationStatus.Calculated : PerformanceEvaluationStatus.Draft;
        evaluation.UpdatedAt = now;

        await SaveAsync(cancellationToken);

        LogCalculated(logger, evaluation.Id, accountId, progressRows.Count, null);

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

    // ----- Actual progress ------------------------------------------------------------------

    /// <summary>
    /// Valid progress recorded against the evaluated employee's own targets within the period.
    /// Progress rows are written when results are verified, so calculation only reads them.
    /// </summary>
    private async Task<List<ProgressRow>> LoadProgressRowsAsync(
        PerformanceEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var period = evaluation.EvaluationPeriod;
        return await db.ActualProgresses.AsNoTracking()
            .Where(a => a.TargetAssignment.EmployeeId == evaluation.EmployeeId
                && a.Status == ActualProgressStatus.Valid
                && a.ProgressDate >= period.StartDate
                && a.ProgressDate <= period.EndDate)
            .Select(a => new ProgressRow(a.Id, a.TargetAssignment.KpiDefinitionId, a.ProgressDate, a.Value, a.Denominator))
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

    private sealed record ProgressRow(Guid ProgressId, Guid KpiDefinitionId, DateOnly ProgressDate, decimal Value, decimal? Denominator);

    private sealed record TargetRow(Guid Id, Guid KpiDefinitionId, bool IsEmployeeLevel, int Version, decimal TargetValue);

    private sealed record ActualAggregate(decimal Value, decimal? Denominator);
}
