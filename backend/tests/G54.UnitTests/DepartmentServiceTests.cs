using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Department;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class DepartmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListForAdminReturnsEveryDepartmentWithSubtreeCounts()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var result = await service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["B", "B-D", "B-D-T"], result.Items.Select(i => i.Code));
        var department = result.Items.Single(i => i.Code == "B-D");
        Assert.Equal("DEPARTMENT", department.LevelType);
        Assert.Equal("ACTIVE", department.Status);
        Assert.Equal(world.Branch.Id, department.ParentDepartmentId);
        Assert.Equal("Branch", department.ParentDepartmentName);
        Assert.Equal(1, department.ChildDepartmentCount);
        Assert.Equal(4, department.EmployeeCount); // M + e1 + e2 + onLeave, terminated excluded
    }

    [Fact]
    public async Task DepartmentScopeOnlySeesOwnSubtree()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.ManagerAccount, "Department");

        var result = await service.GetDepartmentsAsync(world.ManagerAccount, new DepartmentFilterDto());

        Assert.Equal(["B-D", "B-D-T"], result.Items.Select(i => i.Code));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetDepartmentAsync(world.ManagerAccount, world.Branch.Id));
    }

    [Fact]
    public async Task BranchScopeResolvesNearestBranchAncestor()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.ManagerAccount, "Branch");

        var result = await service.GetDepartmentsAsync(world.ManagerAccount, new DepartmentFilterDto());

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task OnlyTheOrganizationReadPermissionGrantsDepartmentData()
    {
        var world = await World.CreateAsync();
        var service = new DepartmentService(
            world.Db,
            new FakePermissions(world.AdminAccount, "All", resource: "evaluation", action: "read"),
            new FixedTime(Now),
            Options.Create(new SystemParametersOptions()));

        // evaluation:read used to open the department data; the dedicated organization:read permission replaced it.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentAsync(world.AdminAccount, world.Department.Id));
    }
    [Fact]
    public async Task OwnScopeOrMissingPermissionIsForbidden()
    {
        var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => world.ServiceFor(world.ManagerAccount, "Own")
            .GetDepartmentsAsync(world.ManagerAccount, new DepartmentFilterDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() => world.ServiceWithoutPermission(world.ManagerAccount)
            .GetDepartmentAsync(world.ManagerAccount, world.Department.Id));
    }

    [Fact]
    public async Task UnknownDepartmentIsNotFound()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDepartmentAsync(world.AdminAccount, Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetOverviewAsync(world.AdminAccount, Guid.NewGuid(), null));
    }

    [Fact]
    public async Task ListFiltersByKeywordLevelStatusAndParent()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var byKeyword = await service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto { Keyword = "team" });
        var byLevel = await service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto { LevelType = "branch" });
        var byParent = await service.GetDepartmentsAsync(
            world.AdminAccount, new DepartmentFilterDto { ParentDepartmentId = world.Department.Id });
        var byStatus = await service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto { Status = "ARCHIVED" });
        var paged = await service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto { Page = 2, PageSize = 2 });

        Assert.Equal(["B-D-T"], byKeyword.Items.Select(i => i.Code));
        Assert.Equal(["B"], byLevel.Items.Select(i => i.Code));
        Assert.Equal(["B-D-T"], byParent.Items.Select(i => i.Code));
        Assert.Empty(byStatus.Items);
        Assert.Equal(0, byStatus.TotalCount);
        Assert.Equal(["B-D-T"], paged.Items.Select(i => i.Code));
        Assert.Equal(3, paged.TotalCount);
    }

    [Fact]
    public async Task ListSummarisesManagerAndHeadcountByJobPositionCode()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");
        var unit = await world.AddDepartmentAsync("UNIT", world.Branch.Id, DepartmentLevelType.Department);
        var teller = world.AddPosition(JobPositionCodes.Teller, "Giao dịch viên");
        var sales = world.AddPosition(JobPositionCodes.Sales, "Chuyên viên Kinh doanh");
        // A title that merely looks like a role must not be counted: only the position code decides.
        var lookalike = world.AddPosition("LEAD", "Trưởng nhóm Kinh doanh - Bán hàng");

        var lead = world.AddEmployee("L001", unit.Id, lookalike.Id, null, EmployeeStatus.Active);
        world.AddEmployee("T101", unit.Id, teller.Id, lead.Id, EmployeeStatus.Active);
        world.AddEmployee("T102", unit.Id, teller.Id, lead.Id, EmployeeStatus.Active);
        world.AddEmployee("S101", unit.Id, sales.Id, lead.Id, EmployeeStatus.Active);
        world.AddEmployee("S102", unit.Id, sales.Id, lead.Id, EmployeeStatus.OnLeave);
        world.AddEmployee("S103", unit.Id, sales.Id, lead.Id, EmployeeStatus.Terminated);
        await world.Db.SaveChangesAsync();

        var result = await service.GetDepartmentsAsync(
            world.AdminAccount, new DepartmentFilterDto { Keyword = "UNIT" });

        var row = Assert.Single(result.Items);
        Assert.Equal("L001", row.ManagerName);
        Assert.Equal(2, row.TellerCount);
        Assert.Equal(1, row.SalesCount); // on-leave and terminated sales staff are not counted
        Assert.Equal(5, row.EmployeeCount); // the contract's non-terminated headcount is unchanged
        Assert.Equal(world.Clock, row.LastSyncAt);
    }

    [Fact]
    public async Task ListLeavesManagerNullAndCountsZeroForAnEmptyDepartment()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var empty = await world.AddDepartmentAsync("EMPTY", world.Branch.Id, DepartmentLevelType.Team);
        var result = await service.GetDepartmentsAsync(
            world.AdminAccount, new DepartmentFilterDto { Keyword = "EMPTY" });

        var row = Assert.Single(result.Items);
        Assert.Equal(empty.Id, row.Id);
        Assert.Null(row.ManagerName);
        Assert.Equal(0, row.TellerCount);
        Assert.Equal(0, row.SalesCount);
        Assert.Equal(0, row.EmployeeCount);
    }

    [Fact]
    public async Task ListAndDetailAgreeOnTheDepartmentManager()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var list = await service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto());
        foreach (var item in list.Items)
        {
            var detail = await service.GetDepartmentAsync(world.AdminAccount, item.Id);
            if (detail.Manager is not null && item.ManagerName is not null)
            {
                Assert.Equal(detail.Manager.FullName, item.ManagerName);
            }
        }

        Assert.Equal("M001", list.Items.Single(i => i.Code == "B-D").ManagerName);
    }

    [Fact]
    public async Task InvalidEnumFilterIsAValidationError()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        await Assert.ThrowsAsync<ValidationException>(
            () => service.GetDepartmentsAsync(world.AdminAccount, new DepartmentFilterDto { Status = "BOGUS" }));
        await Assert.ThrowsAsync<ValidationException>(
            () => service.GetMembersAsync(world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Status = "x" }));
    }

    [Fact]
    public async Task DetailIncludesAncestorsManagerAndCounts()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var detail = await service.GetDepartmentAsync(world.AdminAccount, world.Team.Id);

        Assert.Equal(["B", "B-D"], detail.Ancestors.Select(a => a.Code));
        Assert.Equal(2, detail.DirectEmployeeCount);
        Assert.Equal(2, detail.EmployeeCount);
        Assert.Equal(0, detail.ChildDepartmentCount);
        Assert.Null(detail.Description);
        Assert.NotNull(detail.Manager);
        Assert.Equal("M001", detail.Manager.EmployeeCode); // outside manager of the team's employees

        var parent = await service.GetDepartmentAsync(world.AdminAccount, world.Department.Id);
        Assert.Equal("M001", parent.Manager?.EmployeeCode);
        Assert.Equal(2, parent.DirectEmployeeCount); // M001 and E003; terminated T001 excluded
        Assert.Equal(4, parent.EmployeeCount);
        Assert.Equal("Dept description", parent.Description);
    }

    [Fact]
    public async Task ManagerIsNullWhenDepartmentHasNoActiveEmployees()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var empty = await world.AddDepartmentAsync("EMPTY", world.Branch.Id, DepartmentLevelType.Team);
        var emptyDetail = await service.GetDepartmentAsync(world.AdminAccount, empty.Id);

        Assert.Null(emptyDetail.Manager);
        Assert.Equal(0, emptyDetail.EmployeeCount);
    }

    [Fact]
    public async Task OverviewUsesNullForUnavailableData()
    {
        var world = await World.CreateAsync(withPeriods: false);
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Null(overview.Period);
        Assert.Empty(overview.AvailablePeriods);
        Assert.Null(overview.Performance.Average);
        Assert.Null(overview.Performance.Target);
        Assert.Null(overview.Performance.ChangeFromPrevious);
        Assert.Equal(0, overview.Performance.EvaluatedEmployeeCount);
        Assert.Empty(overview.Performance.Kpis);
        Assert.Null(overview.Operational.Escalated);
        Assert.Null(overview.Operational.CompletedInPeriod);
        Assert.Equal(Now, overview.GeneratedAt);
    }

    [Fact]
    public async Task OverviewPicksNarrowestPeriodContainingTodayAndHidesDayPeriods()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal("2026-10", overview.Period?.Code);
        Assert.Equal("MONTH", overview.Period?.PeriodType);
        Assert.DoesNotContain(overview.AvailablePeriods, p => p.PeriodType == "DAY");
        Assert.Equal(["2026-10", "2026-Q4", "2026-09"], overview.AvailablePeriods.Select(p => p.Code));
    }

    [Fact]
    public async Task OverviewFallsBackToLatestEndedPeriodWhenTodayIsUncovered()
    {
        var world = await World.CreateAsync(clock: new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal("2026-Q4", overview.Period?.Code); // latest period end date wins
    }

    [Fact]
    public async Task OverviewRejectsDayAndUnknownPeriods()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        await Assert.ThrowsAsync<ValidationException>(
            () => service.GetOverviewAsync(world.AdminAccount, world.Department.Id, world.DayPeriod.Id));
        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetOverviewAsync(world.AdminAccount, world.Department.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task OverviewCountsWorkAndOperationalStatesOverSubtree()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal(4, overview.Metrics.EmployeeCount);
        Assert.Equal(3, overview.Metrics.ActiveEmployeeCount);
        Assert.Equal(1, overview.Metrics.OnLeaveEmployeeCount);
        Assert.Equal(4, overview.Metrics.OpenWorkCount); // new, in-progress, overdue in-progress, rejected
        Assert.Equal(2, overview.Metrics.AttentionWorkCount); // overdue + rejected
        Assert.Equal(1, overview.Metrics.PendingReviewCount);
        Assert.Equal(1, overview.Operational.Waiting);
        Assert.Equal(1, overview.Operational.InProgress); // the overdue in-progress work counts as overdue only
        Assert.Equal(1, overview.Operational.Overdue);
        Assert.Equal(1, overview.Operational.CompletedInPeriod);
        Assert.Null(overview.Operational.Escalated);

        Assert.Equal(["Sales", null], overview.WorkMix.Select(m => m.Name));
        Assert.Equal([3, 1], overview.WorkMix.Select(m => m.Count));
        Assert.Null(overview.WorkMix[1].WorkTypeId);
    }

    [Fact]
    public async Task OverviewAveragesScoredEvaluationsAndComputesChange()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal(80m, overview.Performance.Average); // 70 and 90; draft and unscored ignored
        Assert.Equal(2, overview.Performance.EvaluatedEmployeeCount);
        Assert.Equal(5m, overview.Performance.ChangeFromPrevious); // previous month averaged 75
        Assert.Null(overview.Performance.Target);
    }

    [Fact]
    public async Task OverviewComputesKpiActualsAndAchievement()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal(["KPI_AVG", "KPI_NONE", "KPI_SUM"], overview.Performance.Kpis.Select(k => k.Code));
        var sum = overview.Performance.Kpis.Single(k => k.Code == "KPI_SUM");
        Assert.Equal(200m, sum.TargetValue); // latest active version wins
        Assert.Equal(150m, sum.ActualValue); // invalid and out-of-period rows ignored
        Assert.Equal(75m, sum.AchievementPercent);
        Assert.Equal("HIGHER_BETTER", sum.Direction);

        var avg = overview.Performance.Kpis.Single(k => k.Code == "KPI_AVG");
        Assert.Equal(4m, avg.ActualValue);
        Assert.Equal(100m, avg.AchievementPercent); // 4/5*100 = 80 -> LOWER_BETTER 5/4*100 = 125, capped at 100

        var none = overview.Performance.Kpis.Single(k => k.Code == "KPI_NONE");
        Assert.Null(none.ActualValue);
        Assert.Null(none.AchievementPercent);
    }

    [Theory]
    [InlineData(KpiDirection.HigherBetter, 100, 50, null, 50)]
    [InlineData(KpiDirection.HigherBetter, 100, 200, 150, 150)]
    [InlineData(KpiDirection.HigherBetter, 100, 200, null, 200)]
    [InlineData(KpiDirection.LowerBetter, 10, 20, null, 50)]
    [InlineData(KpiDirection.LowerBetter, 10, 0, null, 100)]
    [InlineData(KpiDirection.LowerBetter, 10, 0, 120, 120)]
    [InlineData(KpiDirection.OnTarget, 100, 90, null, 90)]
    [InlineData(KpiDirection.OnTarget, 100, 300, null, 0)]
    public void AchievementFollowsDirection(KpiDirection direction, decimal target, decimal actual, int? max, decimal expected)
    {
        Assert.Equal(expected, DepartmentService.Achievement(direction, target, actual, max));
    }

    [Fact]
    public void AchievementIsNullWithoutActualOrTarget()
    {
        Assert.Null(DepartmentService.Achievement(KpiDirection.HigherBetter, 100, null, null));
        Assert.Null(DepartmentService.Achievement(KpiDirection.HigherBetter, 0, 10, null));
    }

    [Fact]
    public async Task MembersAreScopedFilteredAndPaged()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var all = await service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Sort = "CODE" });
        Assert.Equal(["E001", "E002", "E003", "M001"], all.Items.Select(m => m.EmployeeCode)); // terminated hidden
        var first = all.Items.Single(m => m.EmployeeCode == "E001");
        Assert.Equal("ACTIVE", first.Status);
        Assert.Equal(2, first.OpenWorkCount);
        Assert.Equal(70m, first.PerformanceScore);
        Assert.Equal(90m, all.Items.Single(m => m.EmployeeCode == "E002").PerformanceScore);
        Assert.Null(all.Items.Single(m => m.EmployeeCode == "E003").PerformanceScore); // draft score hidden

        var terminated = await service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Status = "terminated" });
        Assert.Equal(["T001"], terminated.Items.Select(m => m.EmployeeCode));

        var search = await service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Keyword = "e00" });
        Assert.Equal(3, search.TotalCount);

        var paged = await service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Page = 2, PageSize = 3, Sort = "CODE" });
        Assert.Equal(["M001"], paged.Items.Select(m => m.EmployeeCode));
        Assert.Equal(4, paged.TotalCount);
    }

    [Fact]
    public async Task MembersOfDepartmentOutsideScopeAreForbidden()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.ManagerAccount, "Department");

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetMembersAsync(world.ManagerAccount, world.Branch.Id, new DepartmentMemberFilterDto()));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetOverviewAsync(world.ManagerAccount, world.Branch.Id, null));
    }

    // ----- SCR-PRF-02 / SCR-PRF-03 ------------------------------------------------------------

    [Fact]
    public void ComparePeriodFollowsTheMode()
    {
        EvaluationPeriod P(string code, PeriodType type, DateOnly start, DateOnly end) =>
            new() { Id = Guid.NewGuid(), Code = code, Name = code, PeriodType = type, StartDate = start, EndDate = end };
        var oct = P("2026-10", PeriodType.Month, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var sep = P("2026-09", PeriodType.Month, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var aug = P("2026-08", PeriodType.Month, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var lastOct = P("2025-10", PeriodType.Month, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        var q3 = P("2026-Q3", PeriodType.Quarter, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));
        var all = new[] { oct, sep, aug, lastOct, q3 };

        Assert.Same(sep, DepartmentService.ResolveComparePeriod(oct, all, PerformanceCompareMode.Previous));
        Assert.Same(lastOct, DepartmentService.ResolveComparePeriod(oct, all, PerformanceCompareMode.SameLastYear));
        Assert.Null(DepartmentService.ResolveComparePeriod(q3, all, PerformanceCompareMode.Previous));
        Assert.Null(DepartmentService.ResolveComparePeriod(null, all, PerformanceCompareMode.Previous));
    }

    [Fact]
    public async Task OverviewReturnsComparePeriodPreviousAverageAndDeclineCounts()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal("2026-09", overview.ComparePeriod?.Code);
        Assert.Equal(75m, overview.Performance.PreviousAverage);
        Assert.Equal(5m, overview.Performance.ChangeFromPrevious);
        Assert.Equal(2, overview.Metrics.ComparedEmployeeCount); // E001 70 -> 70, E002 80 -> 90
        Assert.Equal(0, overview.Metrics.DeclinedEmployeeCount);
        Assert.Equal(0, overview.Metrics.DeclinedChildUnitCount); // child B-D-T went from 75 to 80
    }

    [Fact]
    public async Task SameLastYearWithoutSuchPeriodNullsEveryComparison()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null, "same_last_year");

        Assert.Null(overview.ComparePeriod);
        Assert.Null(overview.Performance.PreviousAverage);
        Assert.Null(overview.Performance.ChangeFromPrevious);
        Assert.Null(overview.Metrics.DeclinedEmployeeCount);
        Assert.Null(overview.Metrics.ComparedEmployeeCount);
        Assert.All(overview.Performance.Positions, p => Assert.Null(p.PreviousAverage));
        Assert.All(overview.AttentionMembers, m => Assert.Null(m.ChangeFromPrevious));
    }

    [Fact]
    public async Task InvalidCompareOrSortIsAValidationError()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        await Assert.ThrowsAsync<ValidationException>(
            () => service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null, "LAST_WEEK"));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Compare = "x" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Sort = "NAME" }));
    }

    [Fact]
    public async Task OperationalPartsAreDisjointAndCarryPendingReview()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal(1, overview.Operational.InProgress); // W3 only: W2 is in progress but overdue
        Assert.Equal(1, overview.Operational.Overdue);
        Assert.Equal(1, overview.Operational.PendingReview);
        Assert.Equal(overview.Metrics.PendingReviewCount, overview.Operational.PendingReview);
    }

    [Fact]
    public async Task TrendCoversTheConfiguredNumberOfPeriodsOfTheSameType()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var trend = (await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).Performance.Trend;
        Assert.Equal(["2026-09", "2026-10"], trend.Select(t => t.PeriodName));
        Assert.Equal([75m, 80m], trend.Select(t => t.Average));

        world.Parameters.PerfTrendPeriodCount = 1;
        trend = (await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).Performance.Trend;
        Assert.Equal(["2026-10"], trend.Select(t => t.PeriodName));
    }

    [Fact]
    public async Task KpiGroupsFollowTheProfileRootsWithWeightsPreviousAchievementAndCampaignPart()
    {
        var world = await World.CreateAsync();
        await world.SeedProfileGroupsAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var groups = (await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).Performance.KpiGroups;

        Assert.Equal(["Business", "Service", null], groups.Select(g => g.Name));
        var business = groups[0];
        Assert.Equal(60m, Assert.Single(business.Weights).WeightPercent);
        Assert.Equal("Sales", business.Weights[0].JobPositionTitle);
        var sum = Assert.Single(business.Kpis);
        Assert.Equal("KPI_SUM", sum.Code);
        Assert.Equal(150m, sum.ActualValue);
        Assert.Equal(40m, sum.CampaignActualValue); // campaign result on E001's progress; BAU = 110
        Assert.Null(sum.PreviousAchievementPercent); // no target in the compare month

        var avg = Assert.Single(groups[1].Kpis);
        Assert.Null(avg.CampaignActualValue); // only defined for SUM KPIs
        Assert.Equal(["KPI_NONE"], groups[2].Kpis.Select(k => k.Code));
        Assert.Empty(groups[2].Weights);
    }

    [Fact]
    public async Task PositionsAverageGroupScoresAndFlagWeakGroups()
    {
        var world = await World.CreateAsync();
        await world.SeedProfileGroupsAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var position = Assert.Single((await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).Performance.Positions);

        Assert.Equal("Sales", position.JobPositionTitle);
        Assert.Equal(4, position.EmployeeCount);
        Assert.Equal(2, position.EvaluatedEmployeeCount);
        Assert.Equal(80m, position.Average);
        Assert.Equal(75m, position.PreviousAverage);
        Assert.Equal(5m, position.ChangeFromPrevious);
        Assert.Equal(["Business", "Service"], position.Groups.Select(g => g.Name));
        Assert.Equal([85m, 80m], position.Groups.Select(g => g.Average)); // (80+90)/2, (60+100)/2
        Assert.Equal([false, true], position.Groups.Select(g => g.IsWeak)); // threshold 85

        world.Parameters.PerfWeakGroupScore = 90m;
        position = Assert.Single((await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).Performance.Positions);
        Assert.Equal([true, true], position.Groups.Select(g => g.IsWeak));
    }

    [Fact]
    public async Task AttentionListsScoredEmployeesWithReasonsAndRespectsTheThresholds()
    {
        var world = await World.CreateAsync();
        await world.SeedProfileGroupsAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var attention = (await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).AttentionMembers;

        var e1 = Assert.Single(attention);
        Assert.Equal("E001", e1.EmployeeCode);
        Assert.Equal(["LOW_SCORE", "OVERDUE_WORK"], e1.Reasons); // 70 < 80 and W2 is overdue
        Assert.Equal(0m, e1.ChangeFromPrevious);
        Assert.Equal(1, e1.OverdueWorkCount);
        Assert.Equal(new DepartmentWeakestGroupDto("Service", 60m), e1.WeakestGroup);

        world.Parameters.PerfAttentionLowScore = 95m;
        attention = (await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).AttentionMembers;
        Assert.Equal(["E001", "E002"], attention.Select(a => a.EmployeeCode)); // two reasons first

        world.Parameters.PerfAttentionTopN = 1;
        attention = (await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null)).AttentionMembers;
        Assert.Equal(["E001"], attention.Select(a => a.EmployeeCode));
    }

    [Fact]
    public async Task DisputedEvaluationIsLeftOutOfAveragesCountsAndAttention()
    {
        var world = await World.CreateAsync();
        var e2 = await world.Db.Employees.SingleAsync(e => e.EmployeeCode == "E002");
        var month = await world.Db.EvaluationPeriods.SingleAsync(p => p.Code == "2026-10");
        var evaluation = await world.Db.PerformanceEvaluations.SingleAsync(pe => pe.EmployeeId == e2.Id && pe.EvaluationPeriodId == month.Id);
        evaluation.Status = PerformanceEvaluationStatus.Disputed;
        await world.Db.SaveChangesAsync();
        world.Parameters.PerfAttentionLowScore = 95m;
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Department.Id, null);

        Assert.Equal(70m, overview.Performance.Average);
        Assert.Equal(1, overview.Performance.EvaluatedEmployeeCount);
        Assert.Equal(1, overview.Metrics.ComparedEmployeeCount);
        Assert.DoesNotContain(overview.AttentionMembers, m => m.EmployeeCode == "E002");

        var member = (await service.GetMembersAsync(world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto()))
            .Items.Single(m => m.EmployeeCode == "E002");
        Assert.Equal("DISPUTED", member.EvaluationStatus);
        Assert.Null(member.PerformanceScore);
        Assert.Null(member.ChangeFromPrevious);
    }

    [Fact]
    public async Task BranchOverviewListsChildUnitsWithTheirChange()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var overview = await service.GetOverviewAsync(world.AdminAccount, world.Branch.Id, null);

        var unit = Assert.Single(overview.ChildUnits);
        Assert.Equal("B-D", unit.Code);
        Assert.Equal("DEPARTMENT", unit.LevelType);
        Assert.Equal(4, unit.EmployeeCount);
        Assert.Equal(2, unit.EvaluatedEmployeeCount);
        Assert.Equal(80m, unit.Average);
        Assert.Equal(75m, unit.PreviousAverage);
        Assert.Equal(5m, unit.ChangeFromPrevious);
        Assert.Equal(0, overview.Metrics.DeclinedChildUnitCount);

        var team = await service.GetOverviewAsync(world.AdminAccount, world.Team.Id, null);
        Assert.Empty(team.ChildUnits);
        Assert.Null(team.Metrics.DeclinedChildUnitCount);
    }

    [Fact]
    public async Task MembersCarryGroupsChangeWorkCountsAndSortInsidePositions()
    {
        var world = await World.CreateAsync();
        await world.SeedProfileGroupsAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var members = (await service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Sort = "change_desc" })).Items;

        // CHANGE_DESC: E002 +10, E001 0, then rows without a change by code.
        Assert.Equal(["E002", "E001", "E003", "M001"], members.Select(m => m.EmployeeCode));
        var e1 = members.Single(m => m.EmployeeCode == "E001");
        Assert.Equal("CALCULATED", e1.EvaluationStatus);
        Assert.Equal(70m, e1.PreviousPerformanceScore);
        Assert.Equal(0m, e1.ChangeFromPrevious);
        Assert.Equal(["Business", "Service"], e1.Groups.Select(g => g.Name));
        Assert.Equal([80m, 60m], e1.Groups.Select(g => g.Score));
        Assert.Equal([60m, 40m], e1.Groups.Select(g => g.WeightPercent));
        Assert.Equal(e1.Groups[1].ProfileItemId, e1.WeakestProfileItemId);
        Assert.Equal(1, e1.OverdueWorkCount);
        Assert.Equal(0, e1.PendingReviewCount);

        var e2 = members.Single(m => m.EmployeeCode == "E002");
        Assert.Null(e2.WeakestProfileItemId); // lowest group 90 is not below 85
        Assert.Equal(1, e2.PendingReviewCount);
        var e3 = members.Single(m => m.EmployeeCode == "E003");
        Assert.Equal("DRAFT", e3.EvaluationStatus);
        Assert.All(e3.Groups, g => Assert.Null(g.Score));

        var byTotal = await service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Sort = "TOTAL_ASC" });
        Assert.Equal(["E001", "E002", "E003", "M001"], byTotal.Items.Select(m => m.EmployeeCode));
    }

    [Fact]
    public async Task MembersAreGroupedByJobPositionTitle()
    {
        var world = await World.CreateAsync();
        var teller = world.AddPosition("TELLER", "Teller");
        world.AddEmployee("A001", world.Department.Id, teller.Id, null, EmployeeStatus.Active);
        await world.Db.SaveChangesAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var members = await service.GetMembersAsync(
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Sort = "CODE" });

        Assert.Equal(["E001", "E002", "E003", "M001", "A001"], members.Items.Select(m => m.EmployeeCode)); // Sales < Teller
    }

    [Fact]
    public async Task ExportBuildsAWorkbookWithTheDocumentedSheets()
    {
        var world = await World.CreateAsync();
        await world.SeedProfileGroupsAsync();
        var service = world.ServiceFor(world.AdminAccount, "All");

        var file = await service.ExportOverviewAsync(world.AdminAccount, world.Branch.Id, null);

        Assert.Equal("hieu-suat-B-2026-10.xlsx", file.FileName);
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal(["Tong quan", "KPI", "Can bo", "Don vi"], workbook.Worksheets.Select(w => w.Name));
        Assert.Equal("Mã cán bộ", workbook.Worksheet("Can bo").Cell(1, 1).GetString());
        Assert.Equal(5, workbook.Worksheet("Can bo").RowsUsed().Count()); // header + 4 members
    }

    [Fact]
    public async Task ExportOutsideScopeIsForbidden()
    {
        var world = await World.CreateAsync();
        var service = world.ServiceFor(world.ManagerAccount, "Department");

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.ExportOverviewAsync(world.ManagerAccount, world.Branch.Id, null));
    }

    // ----- Test world -----------------------------------------------------------------------

    private sealed class World
    {
        private int _counter;

        private World(AppDbContext db, DateTimeOffset clock)
        {
            Db = db;
            Clock = clock;
        }

        public AppDbContext Db { get; }
        public DateTimeOffset Clock { get; }
        public Department Branch { get; private set; } = null!;
        public Department Department { get; private set; } = null!;
        public Department Team { get; private set; } = null!;
        public EvaluationPeriod DayPeriod { get; private set; } = null!;
        public Guid AdminAccount { get; } = Guid.NewGuid();
        public Guid ManagerAccount { get; private set; }
        public SystemParametersOptions Parameters { get; } = new();

        public static async Task<World> CreateAsync(bool withPeriods = true, DateTimeOffset? clock = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var world = new World(new AppDbContext(options), clock ?? Now);
            await world.SeedAsync(withPeriods);
            return world;
        }

        public DepartmentService ServiceFor(Guid accountId, string scope) =>
            new(Db, new FakePermissions(accountId, scope), new FixedTime(Clock), Options.Create(Parameters));

        public DepartmentService ServiceWithoutPermission(Guid accountId) =>
            new(Db, new FakePermissions(accountId, null), new FixedTime(Clock), Options.Create(Parameters));

        public async Task<Department> AddDepartmentAsync(string code, Guid? parentId, DepartmentLevelType level)
        {
            var department = new Department
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = code,
                ParentDepartmentId = parentId,
                LevelType = level,
                Status = RecordStatus.Active,
                LastSyncAt = Clock,
                UpdatedAt = Clock,
            };
            Db.Departments.Add(department);
            await Db.SaveChangesAsync();
            return department;
        }

        /// <summary>Stages an extra job position; call <see cref="Db"/>.SaveChangesAsync afterwards.</summary>
        public JobPosition AddPosition(string code, string title)
        {
            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = code,
                Title = title,
                Status = RecordStatus.Active,
                LastSyncAt = Clock,
                UpdatedAt = Clock,
            };
            Db.JobPositions.Add(position);
            return position;
        }

        /// <summary>Stages an extra employee; call <see cref="Db"/>.SaveChangesAsync afterwards.</summary>
        public Employee AddEmployee(string code, Guid departmentId, Guid positionId, Guid? managerId, EmployeeStatus status)
        {
            var employee = Employee(code, departmentId, positionId, managerId, status);
            Db.Employees.Add(employee);
            return employee;
        }

        /// <summary>
        /// Profile roots Business (60%: KPI_SUM) and Service (40%: KPI_AVG), stored KPI scores for the month
        /// (E001: 80 / 60, E002: 90 / 100) and one campaign result of 40 on E001's KPI_SUM progress.
        /// </summary>
        public async Task SeedProfileGroupsAsync()
        {
            var profile = await Db.EvaluationProfiles.SingleAsync();
            var sum = await Db.KpiDefinitions.SingleAsync(k => k.Code == "KPI_SUM");
            var avg = await Db.KpiDefinitions.SingleAsync(k => k.Code == "KPI_AVG");
            var business = ProfileItem(profile, null, null, "Business", 60m, 1);
            var service = ProfileItem(profile, null, null, "Service", 40m, 2);
            Db.EvaluationProfileItems.AddRange(
                business,
                service,
                ProfileItem(profile, business.Id, sum.Id, null, 100m, 1),
                ProfileItem(profile, service.Id, avg.Id, null, 100m, 1));

            var month = await Db.EvaluationPeriods.SingleAsync(p => p.Code == "2026-10");
            foreach (var (code, sumScore, avgScore) in new[] { ("E001", 80m, 60m), ("E002", 90m, 100m) })
            {
                var employee = await Db.Employees.SingleAsync(e => e.EmployeeCode == code);
                var evaluation = await Db.PerformanceEvaluations.SingleAsync(
                    pe => pe.EmployeeId == employee.Id && pe.EvaluationPeriodId == month.Id);
                foreach (var (kpi, score) in new[] { (sum, sumScore), (avg, avgScore) })
                {
                    var progress = await Db.ActualProgresses.FirstAsync(a =>
                        a.TargetAssignment.EmployeeId == employee.Id && a.TargetAssignment.KpiDefinitionId == kpi.Id
                        && a.Status == ActualProgressStatus.Valid && a.ProgressDate >= month.StartDate);
                    Db.PerformanceContributions.Add(new PerformanceContribution
                    {
                        Id = Guid.NewGuid(),
                        PerformanceEvaluationId = evaluation.Id,
                        EmployeeId = employee.Id,
                        EvaluationPeriodId = month.Id,
                        ActualProgressId = progress.Id,
                        ComponentScore = score,
                        UpdatedAt = Clock,
                    });
                }
            }

            var e1 = await Db.Employees.SingleAsync(e => e.EmployeeCode == "E001");
            var e1Sum = await Db.ActualProgresses.SingleAsync(a =>
                a.TargetAssignment.EmployeeId == e1.Id && a.TargetAssignment.KpiDefinitionId == sum.Id
                && a.ProgressDate == new DateOnly(2026, 10, 5));
            var campaignWork = Work("CW1", e1.Id, WorkStatus.Done, null, null);
            campaignWork.BusinessCampaignId = Guid.NewGuid();
            var submission = new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = campaignWork.Id,
                SubmitterEmployeeId = e1.Id,
                SubmissionNo = 1,
                SubmittedAt = Clock,
                UpdatedAt = Clock,
                Status = SubmissionStatus.Approved,
            };
            var verification = new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = e1.Id,
                RoundNo = 1,
                UpdatedAt = Clock,
                Decision = VerificationDecision.Approved,
            };
            var verified = new VerifiedWorkResult
            {
                Id = Guid.NewGuid(),
                VerificationId = verification.Id,
                VerifiedAt = Clock,
                UpdatedAt = Clock,
                Status = VerifiedResultStatus.Valid,
            };
            Db.Works.Add(campaignWork);
            Db.ResultSubmissions.Add(submission);
            Db.Verifications.Add(verification);
            Db.VerifiedWorkResults.Add(verified);
            Db.WorkResultItems.Add(new WorkResultItem
            {
                Id = Guid.NewGuid(),
                VerifiedWorkResultId = verified.Id,
                ActualProgressId = e1Sum.Id,
                EmployeeId = e1.Id,
                Value = 40m,
                RawValue = 40m,
                UpdatedAt = Clock,
            });
            await Db.SaveChangesAsync();
        }

        private EvaluationProfileItem ProfileItem(
            EvaluationProfile profile, Guid? parentId, Guid? kpiId, string? name, decimal weight, int order) => new()
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = profile.Id,
                ParentItemId = parentId,
                KpiDefinitionId = kpiId,
                Name = name,
                WeightPercent = weight,
                DisplayOrder = order,
                UpdatedAt = Clock,
            };

        private async Task SeedAsync(bool withPeriods)
        {
            Branch = await AddDepartmentAsync("B", null, DepartmentLevelType.Branch);
            Branch.Name = "Branch";
            Department = await AddDepartmentAsync("B-D", Branch.Id, DepartmentLevelType.Department);
            Team = await AddDepartmentAsync("B-D-T", Department.Id, DepartmentLevelType.Team);
            Team.Name = "Team";
            Team.Description = null;
            Department.Description = "Dept description";

            var profile = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                Code = "EP",
                Name = "EP",
                Version = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                UpdatedAt = Clock,
                Status = EvaluationProfileStatus.Active,
            };
            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "POS",
                Title = "Sales",
                Status = RecordStatus.Active,
                LastSyncAt = Clock,
                UpdatedAt = Clock,
            };
            profile.JobPositionId = position.Id;
            Db.EvaluationProfiles.Add(profile);
            Db.JobPositions.Add(position);

            // M001 manages E001/E002 (in Team) and E003; T001 is terminated; M001 sits in the department itself.
            var manager = Employee("M001", Department.Id, position.Id, null, EmployeeStatus.Active);
            var e1 = Employee("E001", Team.Id, position.Id, manager.Id, EmployeeStatus.Active);
            var e2 = Employee("E002", Team.Id, position.Id, manager.Id, EmployeeStatus.Active);
            var e3 = Employee("E003", Department.Id, position.Id, manager.Id, EmployeeStatus.OnLeave);
            var terminated = Employee("T001", Department.Id, position.Id, manager.Id, EmployeeStatus.Terminated);
            Db.Employees.AddRange(manager, e1, e2, e3, terminated);

            var managerAccountId = Guid.NewGuid();
            ManagerAccount = managerAccountId;
            Db.Accounts.Add(Account(managerAccountId, manager.Id));
            Db.Accounts.Add(Account(AdminAccount, e1.Id));

            var salesType = WorkType("SALES", "Sales");
            Db.WorkTypes.Add(salesType);

            var today = DateOnly.FromDateTime(Clock.UtcDateTime);
            var w1 = Work("W1", e1.Id, WorkStatus.New, today.AddDays(10), salesType.Id);
            var w2 = Work("W2", e1.Id, WorkStatus.InProgress, today.AddDays(-2), salesType.Id); // overdue
            var w3 = Work("W3", e2.Id, WorkStatus.InProgress, today.AddDays(5), salesType.Id);
            var w4 = Work("W4", e2.Id, WorkStatus.Rejected, null, null);
            var w5 = Work("W5", e2.Id, WorkStatus.Done, null, null, updated: Clock.AddDays(-3)); // completed this month
            var w6 = Work("W6", e2.Id, WorkStatus.Cancelled, null, null);
            Db.Works.AddRange(w1, w2, w3, w4, w5, w6);
            Db.ResultSubmissions.Add(new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = w3.Id,
                SubmitterEmployeeId = e2.Id,
                SubmissionNo = 1,
                SubmittedAt = Clock,
                UpdatedAt = Clock,
                Status = SubmissionStatus.Submitted,
            });
            Db.ResultSubmissions.Add(new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = w3.Id,
                SubmitterEmployeeId = e2.Id,
                SubmissionNo = 2,
                SubmittedAt = Clock,
                UpdatedAt = Clock,
                Status = SubmissionStatus.Approved,
            });

            if (withPeriods)
            {
                await SeedPerformanceAsync(profile, e1, e2, e3);
            }

            await Db.SaveChangesAsync();
        }

        private async Task SeedPerformanceAsync(EvaluationProfile profile, Employee e1, Employee e2, Employee e3)
        {
            var quarter = Period("2026-Q4", PeriodType.Quarter, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31));
            var month = Period("2026-10", PeriodType.Month, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
            var previous = Period("2026-09", PeriodType.Month, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
            DayPeriod = Period("D2026-10-18", PeriodType.Day, new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 18));
            Db.EvaluationPeriods.AddRange(quarter, month, previous, DayPeriod);

            Db.PerformanceEvaluations.AddRange(
                Evaluation(e1, month, profile, 70m, PerformanceEvaluationStatus.Calculated),
                Evaluation(e2, month, profile, 90m, PerformanceEvaluationStatus.Approved),
                Evaluation(e3, month, profile, 10m, PerformanceEvaluationStatus.Draft),
                Evaluation(e1, previous, profile, 70m, PerformanceEvaluationStatus.Final),
                Evaluation(e2, previous, profile, 80m, PerformanceEvaluationStatus.Final));
            // e2's score for the month is also shown as draft-hidden on the member list through a second period test:
            Db.PerformanceEvaluations.Add(Evaluation(e3, quarter, profile, null, PerformanceEvaluationStatus.Calculated));

            var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "G", UpdatedAt = Clock, Status = KpiStatus.Active };
            Db.KpiGroups.Add(group);
            var sum = Kpi("KPI_SUM", group, KpiAggregationType.Sum, KpiDirection.HigherBetter, null);
            var avg = Kpi("KPI_AVG", group, KpiAggregationType.Avg, KpiDirection.LowerBetter, 100m);
            var none = Kpi("KPI_NONE", group, KpiAggregationType.Sum, KpiDirection.HigherBetter, null);
            Db.KpiDefinitions.AddRange(sum, avg, none);

            Db.TargetAssignments.AddRange(
                Target(sum, month, 1, 100m, TargetAssignmentStatus.Superseded),
                Target(sum, month, 2, 200m, TargetAssignmentStatus.Active),
                Target(avg, month, 1, 5m, TargetAssignmentStatus.Active),
                Target(none, month, 1, 10m, TargetAssignmentStatus.Active),
                Target(sum, quarter, 1, 999m, TargetAssignmentStatus.Active)); // other period, must not leak

            Db.ActualProgresses.AddRange(
                Progress(sum, e1, month, new DateOnly(2026, 10, 5), 100m, ActualProgressStatus.Valid),
                Progress(sum, e2, month, new DateOnly(2026, 10, 6), 50m, ActualProgressStatus.Valid),
                Progress(sum, e2, month, new DateOnly(2026, 10, 7), 500m, ActualProgressStatus.Invalid),
                Progress(sum, e1, month, new DateOnly(2026, 9, 30), 400m, ActualProgressStatus.Valid),
                Progress(avg, e1, month, new DateOnly(2026, 10, 5), 3m, ActualProgressStatus.Valid),
                Progress(avg, e2, month, new DateOnly(2026, 10, 6), 5m, ActualProgressStatus.Valid));
            await Task.CompletedTask;
        }

        private Employee Employee(string code, Guid departmentId, Guid positionId, Guid? managerId, EmployeeStatus status) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@test.local",
            DepartmentId = departmentId,
            JobPositionId = positionId,
            ManagerId = managerId,
            LastSyncAt = Clock,
            UpdatedAt = Clock,
            Status = status,
        };

        private Account Account(Guid id, Guid employeeId) => new()
        {
            Id = id,
            EmployeeId = employeeId,
            Username = $"user-{++_counter}",
            PasswordHash = "x",
            LastSyncAt = Clock,
            UpdatedAt = Clock,
        };

        private WorkType WorkType(string code, string name)
        {
            return new WorkType
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                UpdatedAt = Clock,
                Status = RecordStatus.Active,
            };
        }

        private Work Work(string code, Guid assignee, WorkStatus status, DateOnly? end, Guid? workTypeId, DateTimeOffset? updated = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = code,
            AssigneeEmployeeId = assignee,
            CreatedBy = assignee,
            Status = status,
            Priority = WorkPriority.Medium,
            PlannedEndDate = end,
            WorkTypeId = workTypeId,
            UpdatedAt = updated ?? Clock,
        };

        private EvaluationPeriod Period(string code, PeriodType type, DateOnly start, DateOnly end) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = type,
            StartDate = start,
            EndDate = end,
            UpdatedAt = Clock,
            Status = PeriodStatus.Open,
        };

        private PerformanceEvaluation Evaluation(
            Employee employee, EvaluationPeriod period, EvaluationProfile profile, decimal? score, PerformanceEvaluationStatus status) => new()
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EvaluationPeriodId = period.Id,
                EvaluationProfileId = profile.Id,
                TotalScore = score,
                Status = status,
                UpdatedAt = Clock,
            };

        private KpiDefinition Kpi(string code, KpiGroup group, KpiAggregationType aggregation, KpiDirection direction, decimal? max) => new()
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = code,
            Name = code,
            Unit = "unit",
            AggregationType = aggregation,
            Direction = direction,
            MaxAchievementPercent = max,
            UpdatedAt = Clock,
            Status = KpiStatus.Active,
        };

        private TargetAssignment Target(
            KpiDefinition kpi, EvaluationPeriod period, int version, decimal value, TargetAssignmentStatus status) => new()
            {
                Id = Guid.NewGuid(),
                KpiDefinitionId = kpi.Id,
                DepartmentId = Department.Id,
                EvaluationPeriodId = period.Id,
                Version = version,
                TargetValue = value,
                Status = status,
                CreatedAt = Clock,
                UpdatedAt = Clock,
            };

        private readonly Dictionary<(Guid Kpi, Guid Employee, Guid Period), TargetAssignment> _employeeTargets = [];

        // Progress hangs off an employee-level target; one per KPI/employee/period, like real data.
        private TargetAssignment EmployeeTarget(KpiDefinition kpi, Employee employee, EvaluationPeriod period)
        {
            var key = (kpi.Id, employee.Id, period.Id);
            if (!_employeeTargets.TryGetValue(key, out var target))
            {
                target = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    KpiDefinitionId = kpi.Id,
                    EmployeeId = employee.Id,
                    EvaluationPeriodId = period.Id,
                    Version = 1,
                    TargetValue = 0,
                    Status = TargetAssignmentStatus.Active,
                    CreatedAt = Clock,
                    UpdatedAt = Clock,
                };
                _employeeTargets[key] = target;
            }

            return target;
        }

        private ActualProgress Progress(
            KpiDefinition kpi, Employee employee, EvaluationPeriod period, DateOnly date, decimal value, ActualProgressStatus status) => new()
            {
                Id = Guid.NewGuid(),
                TargetAssignment = EmployeeTarget(kpi, employee, period),
                ProgressDate = date,
                Value = value,
                Status = status,
                UpdatedAt = Clock,
            };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePermissions(
        Guid accountId, string? scope, string resource = "organization", string action = "read") : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = id == accountId && scope is not null
                ? [new UserPermissionDto(resource, action, scope, $"{resource}:{action}")]
                : [];
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
