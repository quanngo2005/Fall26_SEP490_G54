using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Department;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

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
        Assert.Equal(2, overview.Operational.InProgress);
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

        var all = await service.GetMembersAsync(world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto());
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
            world.AdminAccount, world.Department.Id, new DepartmentMemberFilterDto { Page = 2, PageSize = 3 });
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
            new(Db, new FakePermissions(accountId, scope), new FixedTime(Clock));

        public DepartmentService ServiceWithoutPermission(Guid accountId) =>
            new(Db, new FakePermissions(accountId, null), new FixedTime(Clock));

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
                EvaluationProfileId = profile.Id,
                Status = RecordStatus.Active,
                LastSyncAt = Clock,
                UpdatedAt = Clock,
            };
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
            Db.WorkTypes.Add(salesType.Type);
            Db.WorkflowDefinitions.Add(salesType.Workflow);

            var today = DateOnly.FromDateTime(Clock.UtcDateTime);
            var w1 = Work("W1", e1.Id, WorkStatus.New, today.AddDays(10), salesType.Type.Id);
            var w2 = Work("W2", e1.Id, WorkStatus.InProgress, today.AddDays(-2), salesType.Type.Id); // overdue
            var w3 = Work("W3", e2.Id, WorkStatus.InProgress, today.AddDays(5), salesType.Type.Id);
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

        private (WorkType Type, WorkflowDefinition Workflow) WorkType(string code, string name)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = $"WF-{code}",
                Name = "WF",
                Version = 1,
                UpdatedAt = Clock,
                Status = WorkflowDefinitionStatus.Active,
            };
            return (new WorkType
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                WorkflowDefinitionId = workflow.Id,
                UpdatedAt = Clock,
                Status = RecordStatus.Active,
            }, workflow);
        }

        private Work Work(string code, Guid assignee, WorkStatus status, DateOnly? end, Guid? workTypeId, DateTimeOffset? updated = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = code,
            AssigneeEmployeeId = assignee,
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

        private ActualProgress Progress(
            KpiDefinition kpi, Employee employee, EvaluationPeriod period, DateOnly date, decimal value, ActualProgressStatus status) => new()
            {
                Id = Guid.NewGuid(),
                KpiDefinitionId = kpi.Id,
                EmployeeId = employee.Id,
                EvaluationPeriodId = period.Id,
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

    private sealed class FakePermissions(Guid accountId, string? scope) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = id == accountId && scope is not null
                ? [new UserPermissionDto("evaluation", "read", scope, "EVALUATION_READ")]
                : [];
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
