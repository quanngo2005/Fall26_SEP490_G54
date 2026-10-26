using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.PerformanceDashboard;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed partial class PerformanceDashboardService
{
    private async Task<UnitPerformanceDashboardDto> BuildUnitAsync(
        OrgSnapshot org,
        Guid unitId,
        Guid? periodId,
        CancellationToken cancellationToken)
    {
        var node = org.Nodes[unitId];
        var orgUnit = new DashboardOrgUnitDto(node.Id, node.Code, node.Name, node.Level.ToEnumString());

        var (period, selectable) = await ResolvePeriodAsync(periodId, cancellationToken);
        var now = time.GetUtcNow();
        var availablePeriods = selectable.Select(ToDto).ToList();

        var subtree = org.Subtree(unitId).Where(id => org.Nodes[id].Status != RecordStatus.Archived).ToList();
        var employees = await LoadEmployeesAsync(subtree, cancellationToken);

        if (period is null)
        {
            return new UnitPerformanceDashboardDto(
                orgUnit, null, availablePeriods, employees.Count, 0, null, null, null, [], [], [], [], now);
        }

        var evaluations = await LoadScoredEvaluationsAsync(period.Id, employees, cancellationToken);
        var average = Mean(evaluations.Values.Select(e => e.TotalScore));

        var previous = PreviousPeriod(period, selectable);
        decimal? previousAverage = null;
        if (previous is not null)
        {
            var previousEvaluations = await LoadScoredEvaluationsAsync(previous.Id, employees, cancellationToken);
            previousAverage = Mean(previousEvaluations.Values.Select(e => e.TotalScore));
        }

        var contributions = await LoadKpiContributionsAsync(
            evaluations.Values.Select(e => e.Id).ToList(),
            cancellationToken);

        return new UnitPerformanceDashboardDto(
            orgUnit,
            ToDto(period),
            availablePeriods,
            employees.Count,
            evaluations.Count,
            average,
            previousAverage,
            Change(average, previousAverage),
            BuildUnitGroups(contributions),
            BuildUnitKpis(contributions),
            BuildChildUnits(org, unitId, employees, evaluations),
            BuildMembers(unitId, employees, evaluations, await LoadAnyEvaluationStatusesAsync(period.Id, employees, unitId, cancellationToken)),
            now);
    }

    private async Task<List<UnitEmployee>> LoadEmployeesAsync(List<Guid> departmentIds, CancellationToken cancellationToken) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Terminated && departmentIds.Contains(e.DepartmentId))
            .Select(e => new UnitEmployee(e.Id, e.EmployeeCode, e.FullName, e.DepartmentId, e.JobPosition.Title))
            .ToListAsync(cancellationToken);

    /// <summary>The scored evaluations of the period by employee id.</summary>
    private async Task<Dictionary<Guid, ScoredEvaluation>> LoadScoredEvaluationsAsync(
        Guid periodId,
        List<UnitEmployee> employees,
        CancellationToken cancellationToken)
    {
        var ids = employees.Select(e => e.Id).ToList();
        var rows = await db.PerformanceEvaluations.AsNoTracking()
            .Where(e => e.EvaluationPeriodId == periodId
                && ids.Contains(e.EmployeeId)
                && e.Status != PerformanceEvaluationStatus.Draft
                && e.TotalScore != null)
            .Select(e => new ScoredEvaluation(e.Id, e.EmployeeId, e.Status, e.TotalScore!.Value))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.EmployeeId);
    }

    /// <summary>The status of every evaluation of the period for the members of the node itself (drafts included).</summary>
    private async Task<Dictionary<Guid, PerformanceEvaluationStatus>> LoadAnyEvaluationStatusesAsync(
        Guid periodId,
        List<UnitEmployee> employees,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var ids = employees.Where(e => e.DepartmentId == unitId).Select(e => e.Id).ToList();
        var rows = await db.PerformanceEvaluations.AsNoTracking()
            .Where(e => e.EvaluationPeriodId == periodId && ids.Contains(e.EmployeeId))
            .Select(e => new { e.EmployeeId, e.Status })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.EmployeeId, r => r.Status);
    }

    /// <summary>
    /// Mean, over the employees that have one, of each employee's group score; an employee's group score is the
    /// mean of that employee's non-null KPI scores in the group.
    /// </summary>
    private static List<DashboardUnitGroupDto> BuildUnitGroups(List<KpiContributionRow> contributions) =>
        contributions
            .GroupBy(c => c.KpiGroupId)
            .Select(group =>
            {
                var first = group.First();
                var perEmployee = group
                    .GroupBy(c => c.EmployeeId)
                    .Select(g => Mean(g.Where(c => c.ComponentScore.HasValue).Select(c => c.ComponentScore!.Value)))
                    .Where(score => score.HasValue)
                    .Select(score => score!.Value)
                    .ToList();

                return new DashboardUnitGroupDto(
                    group.Key,
                    first.KpiGroupCode,
                    first.KpiGroupName,
                    Mean(perEmployee),
                    perEmployee.Count);
            })
            .OrderBy(g => g.Code, StringComparer.Ordinal)
            .ToList();

    private static List<DashboardUnitKpiDto> BuildUnitKpis(List<KpiContributionRow> contributions) =>
        contributions
            .GroupBy(c => c.KpiDefinitionId)
            .Select(g =>
            {
                var first = g.First();
                return new DashboardUnitKpiDto(
                    g.Key,
                    first.KpiCode,
                    first.KpiName,
                    first.Unit,
                    first.Direction.ToEnumString(),
                    g.Select(c => c.EmployeeId).Distinct().Count(),
                    Mean(g.Where(c => c.AchievementPercentCapped.HasValue).Select(c => c.AchievementPercentCapped!.Value)),
                    Mean(g.Where(c => c.ComponentScore.HasValue).Select(c => c.ComponentScore!.Value)));
            })
            .OrderBy(k => k.Code, StringComparer.Ordinal)
            .ToList();

    private static List<DashboardChildUnitDto> BuildChildUnits(
        OrgSnapshot org,
        Guid unitId,
        List<UnitEmployee> employees,
        Dictionary<Guid, ScoredEvaluation> evaluations) =>
        org.ChildrenOf(unitId)
            .Select(id => org.Nodes[id])
            .Where(child => child.Status != RecordStatus.Archived)
            .OrderBy(child => child.Code, StringComparer.Ordinal)
            .Select(child =>
            {
                var subtree = org.Subtree(child.Id).ToHashSet();
                var members = employees.Where(e => subtree.Contains(e.DepartmentId)).ToList();
                var scored = members.Where(e => evaluations.ContainsKey(e.Id)).ToList();

                return new DashboardChildUnitDto(
                    child.Id,
                    child.Code,
                    child.Name,
                    child.Level.ToEnumString(),
                    members.Count,
                    scored.Count,
                    Mean(scored.Select(e => evaluations[e.Id].TotalScore)));
            })
            .ToList();

    private static List<DashboardMemberDto> BuildMembers(
        Guid unitId,
        List<UnitEmployee> employees,
        Dictionary<Guid, ScoredEvaluation> evaluations,
        Dictionary<Guid, PerformanceEvaluationStatus> statuses) =>
        employees
            .Where(e => e.DepartmentId == unitId)
            .OrderBy(e => e.Code, StringComparer.Ordinal)
            .Select(e => new DashboardMemberDto(
                e.Id,
                e.Code,
                e.FullName,
                e.JobPositionTitle,
                statuses.TryGetValue(e.Id, out var status) ? status.ToEnumString() : null,
                evaluations.TryGetValue(e.Id, out var scored) ? RoundOrNull(scored.TotalScore) : null))
            .ToList();

    private sealed record UnitEmployee(Guid Id, string Code, string FullName, Guid DepartmentId, string JobPositionTitle);

    private sealed record ScoredEvaluation(Guid Id, Guid EmployeeId, PerformanceEvaluationStatus Status, decimal TotalScore);
}
