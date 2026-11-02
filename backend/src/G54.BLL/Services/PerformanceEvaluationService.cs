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
    IPerformanceScoreCalculator calculator,
    TimeProvider time,
    ILogger<PerformanceEvaluationService> logger) : IPerformanceEvaluationService
{
    private const string PermissionResource = "evaluation";
    private const string PermissionAction = "calculate";
    private static readonly int DepartmentScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Dept);
    private static readonly int BranchScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Branch);
    private static readonly int AllScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.All);

    private static readonly Action<ILogger, Guid, Guid, int, Exception?> LogCalculated =
        LoggerMessage.Define<Guid, Guid, int>(
            LogLevel.Information,
            new EventId(1, "PerformanceCalculated"),
            "Performance calculated: EvaluationId={EvaluationId}, AccountId={AccountId}, KpiRows={KpiRows}");

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
                    .ThenInclude(i => i.KpiDefinition)
                        .ThenInclude(k => k!.KpiGroup)
            .SingleOrDefaultAsync(e => e.Id == evaluationId, cancellationToken)
            ?? throw new NotFoundException("Đánh giá hiệu suất", evaluationId);

        await EnsureInScopeAsync(caller, evaluation.Employee.DepartmentId, cancellationToken);
        EnsureCalculable(evaluation);

        var now = time.GetUtcNow();
        var qualitativeScores = EvaluationService.ParseStoredComment(evaluation.Comment, logger).QualitativeScores;
        var result = (await calculator.CalculateAsync(
            [new PerformanceScoreSubject(evaluation.EmployeeId, evaluation.Employee.DepartmentId, evaluation.EvaluationProfile, qualitativeScores)],
            evaluation.EvaluationPeriod,
            cancellationToken))[evaluation.EmployeeId];
        var contributionIds = await StageAsync(db, evaluation, result, isDraft: false, now, cancellationToken);

        await SaveAsync(cancellationToken);

        LogCalculated(logger, evaluation.Id, accountId, contributionIds.Count, null);

        return new PerformanceEvaluationDto(
            evaluation.Id,
            evaluation.EmployeeId,
            evaluation.EvaluationPeriodId,
            evaluation.EvaluationProfileId,
            evaluation.TotalScore,
            evaluation.Status.ToEnumString(),
            evaluation.CalculatedAt,
            evaluation.UpdatedAt,
            ToGroupDtos(result, contributionIds));
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

    // ----- Persistence ----------------------------------------------------------------------

    /// <summary>
    /// Stages the scored result on the tracked evaluation without saving, so the caller saves it in one unit with
    /// whatever else it changed: the KPI detail rows, the total score and the status. It checks no permission: the caller
    /// has already authorised the action (the form save checks <c>evaluation:submit</c> over the employee's department, the
    /// calculation endpoint checks <c>evaluation:calculate</c>), so it is internal and not part of the public interface. A draft save
    /// leaves a new or Draft evaluation as Draft and keeps an evaluation that is already Calculated as Calculated; a result
    /// without a total score is always Draft; otherwise the evaluation becomes Calculated. Returns the row id of each KPI
    /// that has a detail row.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, Guid>> StageAsync(
        AppDbContext db,
        PerformanceEvaluation evaluation,
        PerformanceScoreResult result,
        bool isDraft,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var contributionIds = await SyncAsync(db, evaluation, result, now, cancellationToken);

        // A draft save never demotes an evaluation that was already completed (Calculated) while it still has a total.
        var keepsCalculated = isDraft
            && evaluation.Status == PerformanceEvaluationStatus.Calculated
            && result.TotalScore.HasValue;

        // The stored score always reflects the latest calculation: a null total clears a stale one.
        evaluation.TotalScore = result.TotalScore;
        evaluation.CalculatedAt = result.TotalScore.HasValue ? now : null;
        evaluation.Status = (isDraft && !keepsCalculated) || !result.TotalScore.HasValue
            ? PerformanceEvaluationStatus.Draft
            : PerformanceEvaluationStatus.Calculated;
        evaluation.UpdatedAt = now;

        return contributionIds;
    }

    /// <summary>The KPI detail of the scored result as the calculation endpoint returns it.</summary>
    private static List<PerformanceGroupContributionDto> ToGroupDtos(
        PerformanceScoreResult result,
        IReadOnlyDictionary<Guid, Guid> contributionIds) =>
        result.Groups
            .Select(group => new PerformanceGroupContributionDto(
                group.EvaluationProfileItemId,
                group.Name,
                group.WeightPercent,
                group.Score,
                group.Kpis
                    .Where(k => k.HasContribution)
                    .OrderBy(k => k.Code, StringComparer.Ordinal)
                    .Select(k => new PerformanceKpiContributionDto(
                        contributionIds[k.KpiDefinitionId],
                        k.KpiDefinitionId,
                        k.Code,
                        k.Name,
                        k.Unit,
                        k.TargetAssignmentId!.Value,
                        k.TargetValue!.Value,
                        k.TargetVersion!.Value,
                        k.ActualValue!.Value,
                        k.ActualDenominator,
                        k.AchievementPercentRaw,
                        k.AchievementPercentCapped,
                        k.ComponentScore))
                    .ToList()))
            .ToList();

    /// <summary>
    /// Makes the KPI rows of the evaluation match the scored result: one row per KPI that has both a target and
    /// progress, updated in place, and rows of KPIs that no longer qualify removed. Risk-event rows are owned by the risk
    /// workflow and are left alone. Returns the row id of each kept KPI.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, Guid>> SyncAsync(
        AppDbContext db,
        PerformanceEvaluation evaluation,
        PerformanceScoreResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.PerformanceContributions
            .Include(c => c.ActualProgress)
                .ThenInclude(a => a!.TargetAssignment)
            .Where(c => c.PerformanceEvaluationId == evaluation.Id && c.RiskEventId == null)
            .ToListAsync(cancellationToken);

        var keptIds = new HashSet<Guid>();
        var idsByKpi = new Dictionary<Guid, Guid>();
        foreach (var kpi in result.Groups.SelectMany(g => g.Kpis).Where(k => k.HasContribution))
        {
            var contribution = existing.FirstOrDefault(c =>
                !keptIds.Contains(c.Id) && c.ActualProgress?.TargetAssignment.KpiDefinitionId == kpi.KpiDefinitionId);
            if (contribution is null)
            {
                contribution = new PerformanceContribution
                {
                    Id = Guid.NewGuid(),
                    PerformanceEvaluationId = evaluation.Id,
                    EmployeeId = evaluation.EmployeeId,
                    EvaluationPeriodId = evaluation.EvaluationPeriodId,
                };
                existing.Add(contribution);
                db.PerformanceContributions.Add(contribution);
            }

            keptIds.Add(contribution.Id);
            idsByKpi[kpi.KpiDefinitionId] = contribution.Id;
            contribution.ActualProgressId = kpi.AnchorProgressId;
            contribution.WeightSnapshot = kpi.ItemWeightPercent;
            contribution.TargetValueSnapshot = kpi.TargetValue;
            contribution.TargetVersionSnapshot = kpi.TargetVersion;
            contribution.ActualValue = kpi.ActualValue;
            contribution.ActualDenominator = kpi.ActualDenominator;
            contribution.AchievementPercentRaw = kpi.AchievementPercentRaw;
            contribution.AchievementPercentCapped = kpi.AchievementPercentCapped;
            contribution.ComponentScore = kpi.ComponentScore;
            contribution.UpdatedAt = now;
        }

        foreach (var stale in existing.Where(c => !keptIds.Contains(c.Id)))
        {
            db.PerformanceContributions.Remove(stale);
        }

        return idsByKpi;
    }

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

    // ----- Private shapes -------------------------------------------------------------------

    private sealed record CallerScope(Guid DepartmentId, int Level);

    private sealed record DepartmentNode(Guid Id, Guid? ParentId, DepartmentLevelType Level);
}
