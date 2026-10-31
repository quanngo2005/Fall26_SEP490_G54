using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class PerformanceDashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 7, 0, 0, TimeSpan.Zero);

    // ----- Personal -------------------------------------------------------------------------

    [Fact]
    public async Task Me_ReturnsTheCallersOwnScoresGroupsAndKpis()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, target: 100m, actual: 80m, raw: 80m, capped: 80m, score: 80m);
        world.AddKpiRow(evaluation, world.Employee, world.KpiKh, target: 10m, actual: 12m, raw: 120m, capped: 100m, score: 100m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal(world.Employee.Id, dto.Employee.Id);
        Assert.Equal("E001", dto.Employee.Code);
        Assert.Equal(world.October.Id, dto.Period!.Id);
        Assert.Equal("CALCULATED", dto.EvaluationStatus);
        Assert.Equal(80m, dto.TotalScore);
        Assert.Equal(Now.AddHours(-1), dto.CalculatedAt);
        var group = Assert.Single(dto.Groups, g => g.Name == "KD");
        Assert.Equal(2, dto.Groups.Count);
        Assert.Equal(70m, group.WeightPercent);
        Assert.Equal(90m, group.Score);
        Assert.Equal(["HDV", "KH"], group.Kpis.Select(k => k.Code).ToArray());
        var hdv = group.Kpis[0];
        Assert.Equal(100m, hdv.TargetValue);
        Assert.Equal(80m, hdv.ActualValue);
        Assert.Equal(80m, hdv.AchievementPercentRaw);
        Assert.Equal(80m, hdv.AchievementPercentCapped);
        Assert.Equal(80m, hdv.Score);
        Assert.Equal("HIGHER_BETTER", hdv.Direction);
        var kh = group.Kpis[1];
        Assert.Equal(120m, kh.AchievementPercentRaw);
        Assert.Equal(100m, kh.AchievementPercentCapped);
        Assert.Equal(Now, dto.GeneratedAt);
    }

    [Fact]
    public async Task Me_IgnoresTheEvaluationsOfOtherEmployees()
    {
        using var world = await World.CreateAsync();
        var mine = world.AddEvaluation(world.Employee, world.October, 60m, PerformanceEvaluationStatus.Calculated);
        var others = world.AddEvaluation(world.OtherEmployee, world.October, 99m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(mine, world.Employee, world.KpiHdv, 100m, 60m, 60m, 60m, 60m);
        world.AddKpiRow(others, world.OtherEmployee, world.KpiHdv, 100m, 99m, 99m, 99m, 99m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal(60m, dto.TotalScore);
        var kpi = Assert.Single(Assert.Single(dto.Groups, g => g.Name == "KD").Kpis);
        Assert.Equal(60m, kpi.Score);
    }

    [Fact]
    public async Task Me_WithoutAnEvaluation_HasNoScoreAndNoGroups()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Null(dto.EvaluationStatus);
        Assert.Null(dto.TotalScore);
        Assert.Null(dto.CalculatedAt);
        Assert.Empty(dto.Groups);
    }

    [Fact]
    public async Task Me_TreatsADraftEvaluationAsNotEvaluated()
    {
        using var world = await World.CreateAsync();
        var draft = world.AddEvaluation(world.Employee, world.October, 70m, PerformanceEvaluationStatus.Draft);
        world.AddKpiRow(draft, world.Employee, world.KpiHdv, 100m, 70m, 70m, 70m, 70m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal("DRAFT", dto.EvaluationStatus);
        Assert.Null(dto.TotalScore);
        Assert.Null(dto.CalculatedAt);
        Assert.Empty(dto.Groups);
    }

    [Fact]
    public async Task Me_WithANullTotal_IsNotScored()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, null, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Null(dto.TotalScore);
        Assert.Empty(dto.Groups);
    }

    [Fact]
    public async Task Me_LeavesOutRiskEventAndProgresslessRows()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 80m, 80m, 80m, 80m);
        world.AddProgresslessRow(evaluation, world.Employee);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Single(Assert.Single(dto.Groups, g => g.Name == "KD").Kpis);
        Assert.Empty(Assert.Single(dto.Groups, g => g.Name == "CLDV").Kpis);
    }

    [Fact]
    public async Task Me_NeverListsARiskEventRowAsAKpi()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 80m, 80m, 80m, 80m);
        world.AddRiskEventRow(evaluation, world.Employee, world.KpiKh);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        var kpi = Assert.Single(Assert.Single(dto.Groups, g => g.Name == "KD").Kpis);
        Assert.Equal("HDV", kpi.Code);
    }

    [Fact]
    public async Task Department_NeverCountsARiskEventRowInKpiAverages()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 80m, 80m, 80m, 80m);
        world.AddRiskEventRow(evaluation, world.Employee, world.KpiKh);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        var kpi = Assert.Single(dto.Kpis);
        Assert.Equal("HDV", kpi.Code);
        Assert.Equal(80m, Assert.Single(dto.Groups).AverageScore);
    }

    [Fact]
    public async Task Me_ListsEveryProfileGroupInDisplayOrder_EvenWithoutKpiRows()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 50m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 50m, 50m, 50m, 50m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal(["KD", "CLDV"], dto.Groups.Select(g => g.Name).ToArray());
        Assert.Equal([70m, 30m], dto.Groups.Select(g => g.WeightPercent).ToArray());
        Assert.NotNull(dto.Groups[0].Score);
        Assert.Null(dto.Groups[1].Score);
        Assert.Empty(dto.Groups[1].Kpis);
    }

    [Fact]
    public async Task Me_ScoresAGroupOverOnlyTheKpisThatHaveAScore()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 50m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 40m, 40m, 40m, 40m);
        world.AddKpiRow(evaluation, world.Employee, world.KpiKh, 10m, 5m, null, null, null);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        var group = Assert.Single(dto.Groups, g => g.Name == "KD");
        Assert.Equal(40m, group.Score);
        Assert.Equal(2, group.Kpis.Count);
    }

    [Fact]
    public async Task Me_WeighsTheKpisOfAGroupByTheirWeight()
    {
        // HDV and KH carry 50 each in the seed; re-weight them to 80 / 20.
        using var world = await World.CreateAsync();
        var hdvLeaf = await world.Db.EvaluationProfileItems.SingleAsync(i => i.KpiDefinitionId == world.KpiHdv.Id);
        var khLeaf = await world.Db.EvaluationProfileItems.SingleAsync(i => i.KpiDefinitionId == world.KpiKh.Id);
        hdvLeaf.WeightPercent = 80m;
        khLeaf.WeightPercent = 20m;
        var evaluation = world.AddEvaluation(world.Employee, world.October, 50m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 50m, 50m, 50m, 50m);
        world.AddKpiRow(evaluation, world.Employee, world.KpiKh, 10m, 10m, 100m, 100m, 100m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        // 50 x 80% + 100 x 20% = 60; the plain mean would be 75.
        Assert.Equal(60m, Assert.Single(dto.Groups, g => g.Name == "KD").Score);
    }

    [Fact]
    public async Task Me_ComparesWithThePreviousPeriodOfTheSameType()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.Employee, world.September, 70.5m, PerformanceEvaluationStatus.Approved);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal(70.5m, dto.PreviousTotalScore);
        Assert.Equal(9.5m, dto.ChangeFromPrevious);
    }

    [Fact]
    public async Task Me_WhenThePreviousPeriodIsOnlyADraft_HasNoComparison()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.Employee, world.September, 70m, PerformanceEvaluationStatus.Draft);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Null(dto.PreviousTotalScore);
        Assert.Null(dto.ChangeFromPrevious);
    }

    [Fact]
    public async Task Me_NeverComparesWithAPeriodOfAnotherType()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.Employee, world.QuarterThree, 10m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Null(dto.PreviousTotalScore);
    }

    [Fact]
    public async Task Me_RoundsToTwoDecimalsAwayFromZero()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80.125m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.Employee, world.September, 70.005m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal(80.13m, dto.TotalScore);
        Assert.Equal(70.01m, dto.PreviousTotalScore);
        Assert.Equal(10.12m, dto.ChangeFromPrevious);
    }

    // ----- Periods --------------------------------------------------------------------------

    [Fact]
    public async Task Period_DefaultsToTheNarrowestPeriodContainingToday()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal(world.October.Id, dto.Period!.Id);
        Assert.Equal("MONTH", dto.Period.PeriodType);
    }

    [Fact]
    public async Task Period_CanBeChosenExplicitly()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.September, 61m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, world.September.Id);

        Assert.Equal(world.September.Id, dto.Period!.Id);
        Assert.Equal(61m, dto.TotalScore);
    }

    [Fact]
    public async Task Period_WhenTodayIsOutsideEveryPeriod_FallsBackToTheLatestEndedOne()
    {
        using var world = await World.CreateAsync(clock: new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Equal(world.October.Id, dto.Period!.Id);
    }

    [Fact]
    public async Task Period_ListsEveryNonDayPeriodNewestFirst()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.DoesNotContain(dto.AvailablePeriods, p => p.PeriodType == "DAY");
        Assert.Equal(dto.AvailablePeriods.OrderByDescending(p => p.StartDate).Select(p => p.Id), dto.AvailablePeriods.Select(p => p.Id));
        Assert.Equal(3, dto.AvailablePeriods.Count);
    }

    [Fact]
    public async Task Period_WhenThereIsNoPeriod_ReturnsEmptyFigures()
    {
        using var world = await World.CreateAsync(withPeriods: false);

        var dto = await world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null);

        Assert.Null(dto.Period);
        Assert.Empty(dto.AvailablePeriods);
        Assert.Null(dto.TotalScore);
        Assert.Empty(dto.Groups);
    }

    [Fact]
    public async Task Period_ADayPeriodIsAValidationError()
    {
        using var world = await World.CreateAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, world.DayPeriod.Id));

        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Period_AnUnknownPeriodIsNotFound()
    {
        using var world = await World.CreateAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    // ----- Employee drill-down --------------------------------------------------------------

    [Fact]
    public async Task Employee_TheCallerMayAlwaysReadTheirOwnId()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Own)
            .GetEmployeeDashboardAsync(world.EmployeeAccount.Id, world.Employee.Id, null);

        Assert.Equal(world.Employee.Id, dto.Employee.Id);
    }

    [Fact]
    public async Task Employee_WithOwnScope_AnotherEmployeeIsForbidden()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.OtherEmployee, world.October, 99m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.Own)
                .GetEmployeeDashboardAsync(world.EmployeeAccount.Id, world.OtherEmployee.Id, null));

        Assert.Equal(403, ex.StatusCode);
        Assert.NotNull(evaluation);
    }

    [Fact]
    public async Task Employee_AManagerMayReadAnEmployeeOfTheirSubtree()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 77m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 77m, 77m, 77m, 77m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept)
            .GetEmployeeDashboardAsync(world.ManagerAccount.Id, world.Employee.Id, null);

        Assert.Equal(77m, dto.TotalScore);
        Assert.Single(Assert.Single(dto.Groups, g => g.Name == "KD").Kpis);
    }

    [Fact]
    public async Task Employee_AManagerCannotReadAnEmployeeOfAnotherDepartment()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.Dept)
                .GetEmployeeDashboardAsync(world.ManagerAccount.Id, world.OtherDeptEmployee.Id, null));
    }

    [Fact]
    public async Task Employee_BranchScopeReachesSiblingDepartments()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Branch)
            .GetEmployeeDashboardAsync(world.ManagerAccount.Id, world.OtherDeptEmployee.Id, null);

        Assert.Equal(world.OtherDeptEmployee.Id, dto.Employee.Id);
    }

    [Fact]
    public async Task Employee_AnUnknownEmployeeIsNotFound()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => world.Service(PermissionScopes.All)
                .GetEmployeeDashboardAsync(world.ManagerAccount.Id, Guid.NewGuid(), null));
    }

    // ----- Department -----------------------------------------------------------------------

    [Fact]
    public async Task Department_AveragesTheScoredEvaluationsOfTheSubtree()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.OtherEmployee, world.October, 90m, PerformanceEvaluationStatus.Approved);
        world.AddEvaluation(world.DeptAManager, world.October, 70m, PerformanceEvaluationStatus.Draft);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Equal("DA", dto.OrgUnit.Code);
        Assert.Equal("DEPARTMENT", dto.OrgUnit.LevelType);
        Assert.Equal(3, dto.EmployeeCount);
        Assert.Equal(2, dto.EvaluatedEmployeeCount);
        Assert.Equal(85m, dto.AverageScore);
    }

    [Fact]
    public async Task Department_HidesDisputedPendingScoresFromAverageAndCounts_ButKeepsTheMemberStatus()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.DeptAManager, world.October, 40m, PerformanceEvaluationStatus.Disputed);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Equal(1, dto.EvaluatedEmployeeCount);
        Assert.Equal(80m, dto.AverageScore);
        var disputed = dto.Members.Single(m => m.EmployeeId == world.DeptAManager.Id);
        Assert.Equal("DISPUTED", disputed.EvaluationStatus);
        Assert.Null(disputed.TotalScore);
    }

    [Fact]
    public async Task Department_ExcludesTerminatedEmployeesFromCountsAndAverages()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.TerminatedEmployee, world.October, 10m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Equal(3, dto.EmployeeCount);
        Assert.Equal(1, dto.EvaluatedEmployeeCount);
        Assert.Equal(80m, dto.AverageScore);
    }

    [Fact]
    public async Task Department_ExcludesEmployeesOutsideTheSubtree()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.OtherDeptEmployee, world.October, 20m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Equal(80m, dto.AverageScore);
    }

    [Fact]
    public async Task Department_WithNoScoredEvaluation_HasNullAverages()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Null(dto.AverageScore);
        Assert.Null(dto.PreviousAverageScore);
        Assert.Null(dto.ChangeFromPrevious);
        Assert.Equal(0, dto.EvaluatedEmployeeCount);
        Assert.Empty(dto.Groups);
        Assert.Empty(dto.Kpis);
    }

    [Fact]
    public async Task Department_ComparesTheAverageWithThePreviousPeriod()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.OtherEmployee, world.October, 90m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.Employee, world.September, 70m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.OtherEmployee, world.September, 80m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Equal(85m, dto.AverageScore);
        Assert.Equal(75m, dto.PreviousAverageScore);
        Assert.Equal(10m, dto.ChangeFromPrevious);
    }

    [Fact]
    public async Task Department_AggregatesKpisAndGroupsPerEmployee()
    {
        using var world = await World.CreateAsync();
        var first = world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        var second = world.AddEvaluation(world.OtherEmployee, world.October, 60m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(first, world.Employee, world.KpiHdv, 100m, 80m, 80m, 80m, 80m);
        world.AddKpiRow(first, world.Employee, world.KpiKh, 10m, 20m, 200m, 100m, 100m);
        world.AddKpiRow(second, world.OtherEmployee, world.KpiHdv, 100m, 40m, 40m, 40m, 40m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        var hdv = Assert.Single(dto.Kpis, k => k.Code == "HDV");
        Assert.Equal(2, hdv.EmployeeCount);
        Assert.Equal(60m, hdv.AverageAchievementPercent);
        Assert.Equal(60m, hdv.AverageScore);
        var kh = Assert.Single(dto.Kpis, k => k.Code == "KH");
        Assert.Equal(1, kh.EmployeeCount);
        Assert.Equal(100m, kh.AverageAchievementPercent);
        Assert.Equal(["HDV", "KH"], dto.Kpis.Select(k => k.Code).ToArray());

        var group = Assert.Single(dto.Groups);
        Assert.Equal("KD", group.Code);
        Assert.Equal(2, group.EmployeeCount);
        Assert.Equal(65m, group.AverageScore);
    }

    [Fact]
    public async Task Department_CountsAnEmployeeOncePerKpi_EvenWithSeveralRows()
    {
        using var world = await World.CreateAsync();
        var evaluation = world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 80m, 80m, 80m, 80m);
        world.AddKpiRow(evaluation, world.Employee, world.KpiHdv, 100m, 60m, 60m, 60m, 60m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Equal(1, Assert.Single(dto.Kpis).EmployeeCount);
    }

    [Fact]
    public async Task Department_ListsDirectChildUnitsWithTheirOwnSubtreeFigures()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.DeptAManager, world.October, 60m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        var child = Assert.Single(dto.ChildUnits);
        Assert.Equal(world.TeamA1.Id, child.Id);
        Assert.Equal("TEAM", child.LevelType);
        Assert.Equal(2, child.EmployeeCount);
        Assert.Equal(1, child.EvaluatedEmployeeCount);
        Assert.Equal(80m, child.AverageScore);
        Assert.Equal(70m, dto.AverageScore);
    }

    [Fact]
    public async Task Department_ChildUnitsLeaveOutArchivedDepartments()
    {
        using var world = await World.CreateAsync();
        world.TeamA1.Status = RecordStatus.Archived;
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        Assert.Empty(dto.ChildUnits);
        Assert.Equal(1, dto.EmployeeCount);
    }

    [Fact]
    public async Task Department_MembersAreOnlyTheEmployeesAttachedDirectlyToTheNode()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.DeptAManager, world.October, 66m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.Employee, world.October, 90m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        var member = Assert.Single(dto.Members);
        Assert.Equal(world.DeptAManager.Id, member.EmployeeId);
        Assert.Equal("CALCULATED", member.EvaluationStatus);
        Assert.Equal(66m, member.TotalScore);
    }

    [Fact]
    public async Task Department_AMemberWithADraftShowsItsStatusButNoScore()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.DeptAManager, world.October, 66m, PerformanceEvaluationStatus.Draft);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null);

        var member = Assert.Single(dto.Members);
        Assert.Equal("DRAFT", member.EvaluationStatus);
        Assert.Null(member.TotalScore);
    }

    [Fact]
    public async Task Department_MembersAreOrderedByEmployeeCode()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.TeamA1.Id, null);

        Assert.Equal(dto.Members.Select(m => m.EmployeeCode).Order(StringComparer.Ordinal), dto.Members.Select(m => m.EmployeeCode));
        Assert.Equal(2, dto.Members.Count);
    }

    [Fact]
    public async Task Department_NeverReturnsARank()
    {
        var shapes = new[]
        {
            typeof(G54.BLL.Dtos.PerformanceDashboard.UnitPerformanceDashboardDto),
            typeof(G54.BLL.Dtos.PerformanceDashboard.PersonalPerformanceDashboardDto),
            typeof(G54.BLL.Dtos.PerformanceDashboard.DashboardMemberDto),
            typeof(G54.BLL.Dtos.PerformanceDashboard.DashboardChildUnitDto),
        };

        foreach (var shape in shapes)
        {
            Assert.DoesNotContain(shape.GetProperties(), p =>
                p.Name.Contains("Rank", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Percentile", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Ordinal", StringComparison.OrdinalIgnoreCase));
        }

        await Task.CompletedTask;
    }

    // ----- Branch ---------------------------------------------------------------------------

    [Fact]
    public async Task Branch_CoversTheWholeBranchTree()
    {
        using var world = await World.CreateAsync();
        world.AddEvaluation(world.Employee, world.October, 80m, PerformanceEvaluationStatus.Calculated);
        world.AddEvaluation(world.OtherDeptEmployee, world.October, 60m, PerformanceEvaluationStatus.Calculated);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Branch).GetBranchDashboardAsync(world.ManagerAccount.Id, world.Branch.Id, null);

        Assert.Equal("BRANCH", dto.OrgUnit.LevelType);
        Assert.Equal(70m, dto.AverageScore);
        Assert.Equal(["DA", "DB"], dto.ChildUnits.Select(c => c.Code).ToArray());
        Assert.Equal(80m, dto.ChildUnits[0].AverageScore);
        Assert.Equal(60m, dto.ChildUnits[1].AverageScore);
    }

    [Fact]
    public async Task Branch_ANodeThatIsNotABranchIsNotFound()
    {
        using var world = await World.CreateAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => world.Service(PermissionScopes.All).GetBranchDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Branch_AnUnknownIdIsNotFound()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => world.Service(PermissionScopes.All).GetBranchDashboardAsync(world.ManagerAccount.Id, Guid.NewGuid(), null));
    }

    [Fact]
    public async Task Branch_DepartmentScopeCannotReadTheBranchAboveIt()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.Dept).GetBranchDashboardAsync(world.ManagerAccount.Id, world.Branch.Id, null));
    }

    [Fact]
    public async Task Department_ANodeThatIsNotAvailableIsNotFound()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => world.Service(PermissionScopes.All).GetDepartmentDashboardAsync(world.ManagerAccount.Id, Guid.NewGuid(), null));
    }

    // ----- Authorization --------------------------------------------------------------------

    [Fact]
    public async Task Department_ADepartmentOutsideTheVisibleSetIsForbidden()
    {
        using var world = await World.CreateAsync();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.Dept).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptB.Id, null));

        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Department_BranchScopeSeesSiblingDepartments()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.Branch).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptB.Id, null);

        Assert.Equal(world.DeptB.Id, dto.OrgUnit.Id);
    }

    [Fact]
    public async Task Department_AllScopeSeesEveryDepartment()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(PermissionScopes.All).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptB.Id, null);

        Assert.Equal(world.DeptB.Id, dto.OrgUnit.Id);
    }

    [Fact]
    public async Task Department_OwnScopeCannotReadAnyDepartment()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.Own).GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null));
    }

    [Fact]
    public async Task Everything_WithoutThePermission_IsForbidden()
    {
        using var world = await World.CreateAsync();
        var service = world.Service(scope: null);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMyDashboardAsync(world.EmployeeAccount.Id, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetEmployeeDashboardAsync(world.EmployeeAccount.Id, world.Employee.Id, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetBranchDashboardAsync(world.ManagerAccount.Id, world.Branch.Id, null));
    }

    [Fact]
    public async Task Everything_ForALockedAccount_IsForbidden()
    {
        using var world = await World.CreateAsync();
        world.EmployeeAccount.IsLocked = true;
        await world.SaveAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null));
    }

    [Fact]
    public async Task Everything_ForAnInactiveEmployee_IsForbidden()
    {
        using var world = await World.CreateAsync();
        world.Employee.Status = EmployeeStatus.Inactive;
        await world.SaveAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.Own).GetMyDashboardAsync(world.EmployeeAccount.Id, null));
    }

    [Fact]
    public async Task Everything_ForAnUnknownAccount_IsForbidden()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(PermissionScopes.All).GetMyDashboardAsync(Guid.NewGuid(), null));
    }

    [Fact]
    public async Task Everything_AFullAccessGrantIsEnough()
    {
        using var world = await World.CreateAsync();
        var service = new PerformanceDashboardService(
            world.Db,
            new FakePermissions([new UserPermissionDto("*", "*", PermissionScopes.All, "FULL_ACCESS")]),
            new FixedTime(Now));

        var dto = await service.GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptB.Id, null);

        Assert.Equal(world.DeptB.Id, dto.OrgUnit.Id);
    }

    [Fact]
    public async Task Everything_AnotherActionOnTheSameResourceDoesNotCount()
    {
        using var world = await World.CreateAsync();
        var service = new PerformanceDashboardService(
            world.Db,
            new FakePermissions([new UserPermissionDto("evaluation", "calculate", PermissionScopes.All, "EVALUATION_CALCULATE")]),
            new FixedTime(Now));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMyDashboardAsync(world.EmployeeAccount.Id, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDepartmentDashboardAsync(world.ManagerAccount.Id, world.DeptA.Id, null));
    }

    [Fact]
    public async Task Everything_AnUnrelatedPermissionDoesNotCount()
    {
        using var world = await World.CreateAsync();
        var service = new PerformanceDashboardService(
            world.Db,
            new FakePermissions([new UserPermissionDto("work", "read", PermissionScopes.All, "WORK_READ")]),
            new FixedTime(Now));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMyDashboardAsync(world.EmployeeAccount.Id, null));
    }

    // ----- Fixture --------------------------------------------------------------------------

    private sealed class World : IDisposable
    {
        private readonly DateTimeOffset _clock;
        private readonly Dictionary<(Guid KpiId, Guid EmployeeId), TargetAssignment> _targets = [];

        private World(DateTimeOffset clock) => _clock = clock;

        public required AppDbContext Db { get; init; }
        public required Department Branch { get; init; }
        public required Department DeptA { get; init; }
        public required Department DeptB { get; init; }
        public required Department TeamA1 { get; init; }
        public required Employee Employee { get; init; }
        public required Employee OtherEmployee { get; init; }
        public required Employee DeptAManager { get; init; }
        public Employee Manager => DeptAManager;
        public required Employee OtherDeptEmployee { get; init; }
        public required Employee TerminatedEmployee { get; init; }
        public required Account EmployeeAccount { get; init; }
        public required Account ManagerAccount { get; init; }
        public required EvaluationPeriod October { get; init; }
        public required EvaluationPeriod September { get; init; }
        public required EvaluationPeriod QuarterThree { get; init; }
        public required EvaluationPeriod DayPeriod { get; init; }
        public required KpiDefinition KpiHdv { get; init; }
        public required KpiDefinition KpiKh { get; init; }
        public required EvaluationProfile Profile { get; init; }

        public static async Task<World> CreateAsync(DateTimeOffset? clock = null, bool withPeriods = true)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new AppDbContext(options);

            var branch = Dept("BR", DepartmentLevelType.Branch, null);
            var deptA = Dept("DA", DepartmentLevelType.Department, branch.Id);
            var deptB = Dept("DB", DepartmentLevelType.Department, branch.Id);
            var team = Dept("TA1", DepartmentLevelType.Team, deptA.Id);
            db.Departments.AddRange(branch, deptA, deptB, team);

            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "POS",
                Title = "Chuyên viên",
                Status = RecordStatus.Active,
                UpdatedAt = Now,
            };
            db.JobPositions.Add(position);

            var employee = Person("E001", team, position);
            var other = Person("E002", team, position);
            var manager = Person("M001", deptA, position);
            var otherDept = Person("E900", deptB, position);
            var terminated = Person("E999", team, position, EmployeeStatus.Terminated);
            db.Employees.AddRange(employee, other, manager, otherDept, terminated);

            var employeeAccount = Login(employee);
            var managerAccount = Login(manager);
            db.Accounts.AddRange(employeeAccount, managerAccount);

            var business = new KpiGroup { Id = Guid.NewGuid(), Code = "KD", Name = "Kinh doanh", Status = KpiStatus.Active, UpdatedAt = Now };
            var quality = new KpiGroup { Id = Guid.NewGuid(), Code = "CLDV", Name = "Chất lượng", Status = KpiStatus.Active, UpdatedAt = Now };
            db.KpiGroups.AddRange(business, quality);

            var hdv = Kpi("HDV", business);
            var kh = Kpi("KH", business);
            var csat = Kpi("CSAT", quality);
            db.KpiDefinitions.AddRange(hdv, kh, csat);

            var profile = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                JobPositionId = position.Id,
                Code = "EP",
                Name = "Profile",
                Version = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Status = EvaluationProfileStatus.Active,
                UpdatedAt = Now,
            };
            db.EvaluationProfiles.Add(profile);
            // Weight tree: root groups KD (70) and CLDV (30); KD holds HDV and KH at 50 each, CLDV holds CSAT.
            var kdRoot = new EvaluationProfileItem { Id = Guid.NewGuid(), EvaluationProfileId = profile.Id, Name = "KD", WeightPercent = 70m, DisplayOrder = 1, UpdatedAt = Now };
            var cldvRoot = new EvaluationProfileItem { Id = Guid.NewGuid(), EvaluationProfileId = profile.Id, Name = "CLDV", WeightPercent = 30m, DisplayOrder = 2, UpdatedAt = Now };
            db.EvaluationProfileItems.AddRange(
                kdRoot,
                cldvRoot,
                new EvaluationProfileItem { Id = Guid.NewGuid(), EvaluationProfileId = profile.Id, ParentItemId = kdRoot.Id, KpiDefinitionId = hdv.Id, WeightPercent = 50m, DisplayOrder = 1, UpdatedAt = Now },
                new EvaluationProfileItem { Id = Guid.NewGuid(), EvaluationProfileId = profile.Id, ParentItemId = kdRoot.Id, KpiDefinitionId = kh.Id, WeightPercent = 50m, DisplayOrder = 2, UpdatedAt = Now },
                new EvaluationProfileItem { Id = Guid.NewGuid(), EvaluationProfileId = profile.Id, ParentItemId = cldvRoot.Id, KpiDefinitionId = csat.Id, WeightPercent = 100m, DisplayOrder = 1, UpdatedAt = Now });

            var october = Period("2026-10", PeriodType.Month, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
            var september = Period("2026-09", PeriodType.Month, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
            var quarterThree = Period("2026-Q3", PeriodType.Quarter, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));
            var day = Period("D2026-10-10", PeriodType.Day, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10));
            if (withPeriods)
            {
                db.EvaluationPeriods.AddRange(quarterThree, september, october, day);
            }

            await db.SaveChangesAsync();

            return new World(clock ?? Now)
            {
                Db = db,
                Branch = branch,
                DeptA = deptA,
                DeptB = deptB,
                TeamA1 = team,
                Employee = employee,
                OtherEmployee = other,
                DeptAManager = manager,
                OtherDeptEmployee = otherDept,
                TerminatedEmployee = terminated,
                EmployeeAccount = employeeAccount,
                ManagerAccount = managerAccount,
                October = october,
                September = september,
                QuarterThree = quarterThree,
                DayPeriod = day,
                KpiHdv = hdv,
                KpiKh = kh,
                Profile = profile,
            };
        }

        public PerformanceDashboardService Service(string? scope) =>
            new(
                Db,
                new FakePermissions(scope is null ? [] : [new UserPermissionDto("evaluation", "read", scope, "EVALUATION_READ")]),
                new FixedTime(_clock));

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public PerformanceEvaluation AddEvaluation(
            Employee employee,
            EvaluationPeriod period,
            decimal? total,
            PerformanceEvaluationStatus status)
        {
            var evaluation = new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EvaluationPeriodId = period.Id,
                EvaluationProfileId = Profile.Id,
                TotalScore = total,
                CalculatedAt = Now.AddHours(-1),
                Status = status,
                UpdatedAt = Now,
            };
            Db.PerformanceEvaluations.Add(evaluation);
            return evaluation;
        }

        /// <summary>A KPI row as the calculation stores it: linked to its KPI through a progress row of a target.</summary>
        public PerformanceContribution AddKpiRow(
            PerformanceEvaluation evaluation,
            Employee employee,
            KpiDefinition kpi,
            decimal target,
            decimal actual,
            decimal? raw,
            decimal? capped,
            decimal? score)
        {
            if (!_targets.TryGetValue((kpi.Id, employee.Id), out var assignment))
            {
                assignment = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    KpiDefinitionId = kpi.Id,
                    KpiDefinition = kpi,
                    EmployeeId = employee.Id,
                    EvaluationPeriodId = evaluation.EvaluationPeriodId,
                    Version = 1,
                    TargetValue = target,
                    Status = TargetAssignmentStatus.Active,
                    CreatedAt = Now,
                    UpdatedAt = Now,
                };
                Db.TargetAssignments.Add(assignment);
                _targets[(kpi.Id, employee.Id)] = assignment;
            }

            var progress = new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = assignment.Id,
                TargetAssignment = assignment,
                ProgressDate = new DateOnly(2026, 10, 10),
                Value = actual,
                Status = ActualProgressStatus.Valid,
                UpdatedAt = Now,
            };
            Db.ActualProgresses.Add(progress);

            var row = new PerformanceContribution
            {
                Id = Guid.NewGuid(),
                PerformanceEvaluationId = evaluation.Id,
                EmployeeId = employee.Id,
                EvaluationPeriodId = evaluation.EvaluationPeriodId,
                ActualProgressId = progress.Id,
                ActualProgress = progress,
                TargetValueSnapshot = target,
                TargetVersionSnapshot = 1,
                ActualValue = actual,
                AchievementPercentRaw = raw,
                AchievementPercentCapped = capped,
                WeightSnapshot = 70m,
                ComponentScore = score,
                UpdatedAt = Now,
            };
            Db.PerformanceContributions.Add(row);
            return row;
        }

        /// <summary>A contribution without progress: it has no KPI.</summary>
        public PerformanceContribution AddProgresslessRow(PerformanceEvaluation evaluation, Employee employee)
        {
            var row = new PerformanceContribution
            {
                Id = Guid.NewGuid(),
                PerformanceEvaluationId = evaluation.Id,
                EmployeeId = employee.Id,
                EvaluationPeriodId = evaluation.EvaluationPeriodId,
                ComponentScore = -5m,
                UpdatedAt = Now,
            };
            Db.PerformanceContributions.Add(row);
            return row;
        }

        /// <summary>
        /// A penalty row as a risk event produces it: linked to the risk event and, by mistake or design, also carrying
        /// progress. It must never be listed as a KPI.
        /// </summary>
        public PerformanceContribution AddRiskEventRow(PerformanceEvaluation evaluation, Employee employee, KpiDefinition kpi)
        {
            var risk = new RiskEvent
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                RecordedBy = Manager.Id,
                Code = $"RE-{Guid.NewGuid():N}"[..10],
                EventType = "PROCEDURE_VIOLATION",
                Description = "Risk event",
                OccurredAt = Now,
                Severity = RiskEventSeverity.Medium,
                Status = RiskEventStatus.Confirmed,
                UpdatedAt = Now,
            };
            Db.RiskEvents.Add(risk);

            var row = AddKpiRow(evaluation, employee, kpi, 100m, 1m, 1m, 1m, -5m);
            row.RiskEventId = risk.Id;
            return row;
        }

        public void Dispose() => Db.Dispose();

        private static Department Dept(string code, DepartmentLevelType level, Guid? parentId) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            LevelType = level,
            ParentDepartmentId = parentId,
            Status = RecordStatus.Active,
            UpdatedAt = Now,
        };

        private static Employee Person(string code, Department department, JobPosition position, EmployeeStatus status = EmployeeStatus.Active) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Nhân viên {code}",
            Email = $"{code.ToLowerInvariant()}@example.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = status,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };

        private static Account Login(Employee employee) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = employee.Email,
            PasswordHash = "hash",
            LastSyncAt = Now,
            UpdatedAt = Now,
        };

        private static KpiDefinition Kpi(string code, KpiGroup group) => new()
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = code,
            Name = code,
            Unit = "unit",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };

        private static EvaluationPeriod Period(string code, PeriodType type, DateOnly start, DateOnly end) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = type,
            StartDate = start,
            EndDate = end,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePermissions(IReadOnlyList<UserPermissionDto> granted) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(granted);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
