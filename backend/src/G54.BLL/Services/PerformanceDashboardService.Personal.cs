using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.PerformanceDashboard;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed partial class PerformanceDashboardService
{
    private async Task<PersonalPerformanceDashboardDto> BuildPersonalAsync(
        Guid employeeId,
        Guid? periodId,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new DashboardEmployeeDto(
                e.Id,
                e.EmployeeCode,
                e.FullName,
                e.DepartmentId,
                e.Department.Name,
                e.JobPosition.Title))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Nhân viên", employeeId);

        var (period, selectable) = await ResolvePeriodAsync(periodId, cancellationToken);
        var now = time.GetUtcNow();
        var availablePeriods = selectable.Select(ToDto).ToList();

        if (period is null)
        {
            return new PersonalPerformanceDashboardDto(
                employee, null, availablePeriods, null, null, null, null, null, [], now);
        }

        var evaluation = await LoadEvaluationAsync(employeeId, period.Id, cancellationToken);
        var scored = IsScored(evaluation?.Status, evaluation?.TotalScore);
        var total = scored ? RoundOrNull(evaluation!.TotalScore) : null;

        var previous = PreviousPeriod(period, selectable);
        decimal? previousTotal = null;
        if (previous is not null)
        {
            var previousEvaluation = await LoadEvaluationAsync(employeeId, previous.Id, cancellationToken);
            previousTotal = IsScored(previousEvaluation?.Status, previousEvaluation?.TotalScore)
                ? RoundOrNull(previousEvaluation!.TotalScore)
                : null;
        }

        var groups = scored
            ? await BuildGroupsAsync(evaluation!, cancellationToken)
            : [];

        return new PersonalPerformanceDashboardDto(
            employee,
            ToDto(period),
            availablePeriods,
            evaluation?.Status.ToEnumString(),
            total,
            previousTotal,
            Change(total, previousTotal),
            scored ? evaluation!.CalculatedAt : null,
            groups,
            now);
    }

    private static bool IsScored(PerformanceEvaluationStatus? status, decimal? totalScore) =>
        status.HasValue && status.Value != PerformanceEvaluationStatus.Draft && totalScore.HasValue;

    private Task<EvaluationRow?> LoadEvaluationAsync(
        Guid employeeId,
        Guid periodId,
        CancellationToken cancellationToken) =>
        db.PerformanceEvaluations.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId && e.EvaluationPeriodId == periodId)
            .Select(e => new EvaluationRow(e.Id, e.EvaluationProfileId, e.Status, e.TotalScore, e.CalculatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The profile's groups in display order, each with the KPIs the calculation stored a contribution for.
    /// A group's score is the mean of its non-null KPI scores.
    /// </summary>
    private async Task<List<DashboardGroupDto>> BuildGroupsAsync(
        EvaluationRow evaluation,
        CancellationToken cancellationToken)
    {
        var items = await db.EvaluationProfileItems.AsNoTracking()
            .Where(i => i.EvaluationProfileId == evaluation.ProfileId)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new { i.KpiGroupId, i.KpiGroup.Code, i.KpiGroup.Name, i.WeightPercent })
            .ToListAsync(cancellationToken);

        var contributions = await LoadKpiContributionsAsync([evaluation.Id], cancellationToken);
        var byGroup = contributions.ToLookup(c => c.KpiGroupId);

        return items
            .Select(item =>
            {
                var kpis = byGroup[item.KpiGroupId]
                    .OrderBy(c => c.KpiCode, StringComparer.Ordinal)
                    .ToList();

                return new DashboardGroupDto(
                    item.KpiGroupId,
                    item.Code,
                    item.Name,
                    item.WeightPercent,
                    Mean(kpis.Where(k => k.ComponentScore.HasValue).Select(k => k.ComponentScore!.Value)),
                    kpis.Select(k => new DashboardKpiDto(
                        k.KpiDefinitionId,
                        k.KpiCode,
                        k.KpiName,
                        k.Unit,
                        k.Direction.ToEnumString(),
                        RoundOrNull(k.TargetValue),
                        RoundOrNull(k.ActualValue),
                        RoundOrNull(k.AchievementPercentRaw),
                        RoundOrNull(k.AchievementPercentCapped),
                        RoundOrNull(k.ComponentScore))).ToList());
            })
            .ToList();
    }

    /// <summary>
    /// The KPI rows the calculation stored for the evaluations. A row reaches its KPI through the progress it was
    /// calculated from; rows without progress (risk events) are not KPI rows.
    /// </summary>
    private async Task<List<KpiContributionRow>> LoadKpiContributionsAsync(
        List<Guid> evaluationIds,
        CancellationToken cancellationToken) =>
        await db.PerformanceContributions.AsNoTracking()
            .Where(c => c.PerformanceEvaluationId != null
                && evaluationIds.Contains(c.PerformanceEvaluationId.Value)
                && c.RiskEventId == null
                && c.ActualProgressId != null)
            .Select(c => new KpiContributionRow(
                c.PerformanceEvaluationId!.Value,
                c.EmployeeId,
                c.ActualProgress!.TargetAssignment.KpiDefinitionId,
                c.ActualProgress.TargetAssignment.KpiDefinition.KpiGroupId,
                c.ActualProgress.TargetAssignment.KpiDefinition.KpiGroup.Code,
                c.ActualProgress.TargetAssignment.KpiDefinition.KpiGroup.Name,
                c.ActualProgress.TargetAssignment.KpiDefinition.Code,
                c.ActualProgress.TargetAssignment.KpiDefinition.Name,
                c.ActualProgress.TargetAssignment.KpiDefinition.Unit,
                c.ActualProgress.TargetAssignment.KpiDefinition.Direction,
                c.TargetValueSnapshot,
                c.ActualValue,
                c.AchievementPercentRaw,
                c.AchievementPercentCapped,
                c.ComponentScore))
            .ToListAsync(cancellationToken);

    private sealed record EvaluationRow(
        Guid Id,
        Guid ProfileId,
        PerformanceEvaluationStatus Status,
        decimal? TotalScore,
        DateTimeOffset? CalculatedAt);

    private sealed record KpiContributionRow(
        Guid EvaluationId,
        Guid EmployeeId,
        Guid KpiDefinitionId,
        Guid KpiGroupId,
        string KpiGroupCode,
        string KpiGroupName,
        string KpiCode,
        string KpiName,
        string Unit,
        KpiDirection Direction,
        decimal? TargetValue,
        decimal? ActualValue,
        decimal? AchievementPercentRaw,
        decimal? AchievementPercentCapped,
        decimal? ComponentScore);
}
