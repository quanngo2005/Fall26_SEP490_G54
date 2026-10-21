using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Performance;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class PerformanceEvaluationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly PeriodStart = new(2026, 10, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 10, 31);
    private static readonly DateTimeOffset Day10 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day11 = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    // ----- Calculation ----------------------------------------------------------------------

    [Fact]
    public async Task Calculate_WhenFormulaReturnsNoScores_StoresSnapshotsAndLeavesScoresNull()
    {
        using var world = await World.CreateAsync();
        var target = world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiSum, 10m, null, Day10);
        world.AddResult(world.KpiSum, 30m, null, Day11);
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Null(dto.TotalScore);
        Assert.Equal("DRAFT", dto.Status);
        Assert.Null(dto.CalculatedAt);
        var group = Assert.Single(dto.Groups);
        Assert.Equal(70m, group.WeightPercent);
        Assert.Null(group.ComponentScore);
        var kpi = Assert.Single(group.Kpis);
        Assert.Equal(world.KpiSum.Id, kpi.KpiDefinitionId);
        Assert.Equal(target.Id, kpi.TargetAssignmentId);
        Assert.Equal(100m, kpi.TargetValue);
        Assert.Equal(1, kpi.TargetVersion);
        Assert.Equal(40m, kpi.ActualValue);
        Assert.Null(kpi.ActualDenominator);
        Assert.Null(kpi.AchievementPercentRaw);
        Assert.Null(kpi.AchievementPercentCapped);
        Assert.Null(kpi.ComponentScore);

        var rows = await world.Db.PerformanceContributions.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        var groupRow = Assert.Single(rows, r => r.EvaluationProfileItemId.HasValue);
        var kpiRow = Assert.Single(rows, r => r.KpiDefinitionId.HasValue);
        Assert.Equal(70m, groupRow.WeightSnapshot);
        Assert.Equal(groupRow.Id, kpiRow.ParentContributionId);
        Assert.Equal(world.Employee.Id, kpiRow.EmployeeId);
        Assert.Equal(world.Period.Id, kpiRow.EvaluationPeriodId);
        Assert.Null(kpiRow.WeightSnapshot);

        var stored = await world.Db.PerformanceEvaluations.AsNoTracking().SingleAsync();
        Assert.Null(stored.TotalScore);
        Assert.Equal(PerformanceEvaluationStatus.Draft, stored.Status);
    }

    [Fact]
    public async Task Calculate_ConvertsVerifiedItemsToDayLevelActualProgress()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        var first = world.AddResult(world.KpiSum, 10m, null, Day10);
        var second = world.AddResult(world.KpiSum, 30m, null, Day11);
        await world.SaveAsync();

        await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        var progress = await world.Db.ActualProgresses.AsNoTracking().OrderBy(p => p.ProgressDate).ToListAsync();
        Assert.Equal(2, progress.Count);
        Assert.Equal(first.Id, progress[0].SourceItemId);
        Assert.Equal(second.Id, progress[1].SourceItemId);
        Assert.Equal(world.DayPeriods[DateOnly.FromDateTime(Day10.UtcDateTime)], progress[0].EvaluationPeriodId);
        Assert.Equal(new DateOnly(2026, 10, 11), progress[1].ProgressDate);
        Assert.All(progress, p =>
        {
            Assert.Equal(world.Employee.Id, p.EmployeeId);
            Assert.Equal(ActualProgressStatus.Valid, p.Status);
            Assert.Equal(world.KpiSum.Id, p.KpiDefinitionId);
            Assert.Null(p.PerformanceContributionId);
        });
    }

    [Fact]
    public async Task Calculate_IsIdempotent_AndNeverDuplicatesProgressOrContributions()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiSum, 10m, null, Day10);
        await world.SaveAsync();

        var first = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);
        var second = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(1, await world.Db.ActualProgresses.CountAsync());
        Assert.Equal(2, await world.Db.PerformanceContributions.CountAsync());
        Assert.Equal(first.Groups[0].ContributionId, second.Groups[0].ContributionId);
        Assert.Equal(first.Groups[0].Kpis[0].ContributionId, second.Groups[0].Kpis[0].ContributionId);
        Assert.Equal(10m, second.Groups[0].Kpis[0].ActualValue);
    }

    [Fact]
    public async Task Calculate_PicksUpNewVerifiedResultsOnRecalculation()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiSum, 10m, null, Day10);
        await world.SaveAsync();
        await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        world.AddResult(world.KpiSum, 5m, null, Day11);
        await world.SaveAsync();
        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(15m, dto.Groups[0].Kpis[0].ActualValue);
        Assert.Equal(2, await world.Db.ActualProgresses.CountAsync());
        Assert.Equal(2, await world.Db.PerformanceContributions.CountAsync());
    }

    [Fact]
    public async Task Calculate_IgnoresResultsThatAreNotEligible()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddTarget(world.KpiRatio, 10m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiSum, 5m, null, Day10);
        world.AddResult(world.KpiSum, 100m, null, Day10, resultStatus: VerifiedResultStatus.Invalid);
        world.AddResult(world.KpiSum, 100m, null, new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        world.AddResult(world.KpiSum, 100m, null, new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.Zero));
        world.AddResult(world.KpiSum, 100m, null, Day10, submitter: world.OtherEmployee);
        world.AddResult(world.KpiSum, -1m, null, Day10);
        world.AddResult(world.KpiSum, 100m, null, new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero));
        world.AddResult(world.KpiRatio, 1m, null, Day10);
        world.AddResult(world.KpiRatio, 1m, 0m, Day10);
        world.AddResult(world.KpiSum, 7m, 3m, Day10);
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        var progress = await world.Db.ActualProgresses.AsNoTracking().SingleAsync();
        Assert.Equal(5m, progress.Value);
        var kpi = Assert.Single(Assert.Single(dto.Groups).Kpis);
        Assert.Equal(world.KpiSum.Id, kpi.KpiDefinitionId);
        Assert.Equal(5m, kpi.ActualValue);
    }

    [Fact]
    public async Task Calculate_DoesNotReuseResultsAlreadyConvertedForAnotherEmployeeRow()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        var item = world.AddResult(world.KpiSum, 10m, null, Day10);
        world.Db.ActualProgresses.Add(new ActualProgress
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = world.KpiSum.Id,
            EmployeeId = world.Employee.Id,
            EvaluationPeriodId = world.DayPeriods[DateOnly.FromDateTime(Day10.UtcDateTime)],
            ProgressDate = DateOnly.FromDateTime(Day10.UtcDateTime),
            Value = 10m,
            SourceItemId = item.Id,
            Status = ActualProgressStatus.Valid,
            UpdatedAt = Now,
        });
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(1, await world.Db.ActualProgresses.CountAsync());
        Assert.Equal(10m, dto.Groups[0].Kpis[0].ActualValue);
    }

    [Fact]
    public async Task Calculate_SkipsKpisWithoutTargetOrWithoutData()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddTarget(world.KpiAvg, 5m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiRatio, 1m, 2m, Day10);
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Empty(Assert.Single(dto.Groups).Kpis);
        Assert.Equal(1, await world.Db.PerformanceContributions.CountAsync());
    }

    [Fact]
    public async Task Calculate_AggregatesByKpiAggregationType()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiRatio, 10m, employeeLevel: false, version: 1);
        world.AddTarget(world.KpiAvg, 5m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiRatio, 2m, 10m, Day10);
        world.AddResult(world.KpiRatio, 3m, 10m, Day11);
        world.AddResult(world.KpiAvg, 4m, null, Day10);
        world.AddResult(world.KpiAvg, 5m, null, Day11);
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        var kpis = Assert.Single(dto.Groups).Kpis;
        var ratio = Assert.Single(kpis, k => k.KpiDefinitionId == world.KpiRatio.Id);
        Assert.Equal(5m, ratio.ActualValue);
        Assert.Equal(20m, ratio.ActualDenominator);
        var avg = Assert.Single(kpis, k => k.KpiDefinitionId == world.KpiAvg.Id);
        Assert.Equal(4.5m, avg.ActualValue);
        Assert.Null(avg.ActualDenominator);
    }

    [Fact]
    public async Task Calculate_RemovesContributionsOfKpisThatLostTheirData()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiSum, 10m, null, Day10);
        await world.SaveAsync();
        await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        foreach (var row in await world.Db.ActualProgresses.ToListAsync())
        {
            row.Status = ActualProgressStatus.Invalid;
        }

        await world.Db.SaveChangesAsync();
        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Empty(Assert.Single(dto.Groups).Kpis);
        var remaining = await world.Db.PerformanceContributions.AsNoTracking().SingleAsync();
        Assert.True(remaining.EvaluationProfileItemId.HasValue);
    }

    // ----- Targets --------------------------------------------------------------------------

    [Fact]
    public async Task Calculate_PrefersEmployeeTargetOverDepartmentTarget()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddTarget(world.KpiSum, 120m, employeeLevel: false, version: 2);
        var employeeTarget = world.AddTarget(world.KpiSum, 80m, employeeLevel: true, version: 1);
        world.AddTarget(world.KpiSum, 999m, employeeLevel: true, version: 5, status: TargetAssignmentStatus.Draft);
        world.AddTarget(world.KpiSum, 888m, employeeLevel: true, version: 6, status: TargetAssignmentStatus.Superseded);
        world.AddResult(world.KpiSum, 10m, null, Day10);
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        var kpi = Assert.Single(Assert.Single(dto.Groups).Kpis);
        Assert.Equal(employeeTarget.Id, kpi.TargetAssignmentId);
        Assert.Equal(80m, kpi.TargetValue);
    }

    [Fact]
    public async Task Calculate_UsesHighestActiveDepartmentTargetVersion()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddTarget(world.KpiSum, 120m, employeeLevel: false, version: 2);
        world.AddTarget(world.KpiSum, 777m, employeeLevel: false, version: 3, departmentId: world.DeptB.Id);
        world.AddResult(world.KpiSum, 10m, null, Day10);
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        var kpi = Assert.Single(Assert.Single(dto.Groups).Kpis);
        Assert.Equal(120m, kpi.TargetValue);
        Assert.Equal(2, kpi.TargetVersion);
    }

    // ----- Scoring --------------------------------------------------------------------------

    [Fact]
    public async Task Calculate_WithDefinedFormula_RoundsScoresAndMarksCalculated()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiSum, 100m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiSum, 10m, null, Day10);
        await world.SaveAsync();
        var formula = new FakeScoreFormula(
            new KpiScoreResult(12.34565m, 12.34555m, 1.00005m),
            groupScore: 2.00015m,
            totalScore: 1.23445m);

        var dto = await world.Service(formula).CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(1.2344m, dto.TotalScore);
        Assert.Equal("CALCULATED", dto.Status);
        Assert.Equal(Now, dto.CalculatedAt);
        var group = Assert.Single(dto.Groups);
        Assert.Equal(2.0002m, group.ComponentScore);
        var kpi = Assert.Single(group.Kpis);
        Assert.Equal(12.3456m, kpi.AchievementPercentRaw);
        Assert.Equal(12.3456m, kpi.AchievementPercentCapped);
        Assert.Equal(1.0000m, kpi.ComponentScore);

        var stored = await world.Db.PerformanceEvaluations.AsNoTracking().SingleAsync();
        Assert.Equal(1.2344m, stored.TotalScore);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, stored.Status);
        Assert.Equal(Now, stored.CalculatedAt);
    }

    [Fact]
    public async Task Calculate_PassesTargetActualAndWeightToTheFormula()
    {
        using var world = await World.CreateAsync();
        world.AddTarget(world.KpiRatio, 10m, employeeLevel: false, version: 1);
        world.AddResult(world.KpiRatio, 2m, 10m, Day10);
        await world.SaveAsync();
        var formula = new FakeScoreFormula(new KpiScoreResult(null, null, null), null, null);

        await world.Service(formula).CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        var kpiInput = Assert.Single(formula.KpiInputs);
        Assert.Equal(world.KpiRatio.Id, kpiInput.KpiDefinitionId);
        Assert.Equal(KpiDirection.LowerBetter, kpiInput.Direction);
        Assert.Equal(KpiAggregationType.Ratio, kpiInput.AggregationType);
        Assert.Equal(120m, kpiInput.MaxAchievementPercent);
        Assert.Equal(10m, kpiInput.TargetValue);
        Assert.Equal(2m, kpiInput.ActualValue);
        Assert.Equal(10m, kpiInput.ActualDenominator);
        var groupInput = Assert.Single(formula.GroupInputs);
        Assert.Equal(70m, groupInput.WeightPercent);
        Assert.Single(groupInput.Kpis);
        var total = Assert.Single(formula.TotalInputs);
        Assert.Equal(70m, Assert.Single(total).WeightPercent);
    }

    [Fact]
    public async Task Calculate_WhenTotalIsNull_ClearsTheStaleScore()
    {
        using var world = await World.CreateAsync();
        world.Evaluation.Status = PerformanceEvaluationStatus.Calculated;
        world.Evaluation.TotalScore = 50m;
        world.Evaluation.CalculatedAt = Now.AddDays(-1);
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Null(dto.TotalScore);
        Assert.Null(dto.CalculatedAt);
        Assert.Equal("DRAFT", dto.Status);
    }

    [Fact]
    public async Task Calculate_AllowsRecalculationOfACalculatedEvaluation()
    {
        using var world = await World.CreateAsync();
        world.Evaluation.Status = PerformanceEvaluationStatus.Calculated;
        world.Evaluation.TotalScore = 50m;
        await world.SaveAsync();
        var formula = new FakeScoreFormula(new KpiScoreResult(null, null, null), null, 75m);

        var dto = await world.Service(formula).CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal("CALCULATED", dto.Status);
        Assert.Equal(75m, dto.TotalScore);
        Assert.Equal(Now, dto.CalculatedAt);
    }

    // ----- Errors ---------------------------------------------------------------------------

    [Fact]
    public async Task Calculate_WhenEvaluationDoesNotExist_ThrowsNotFound()
    {
        using var world = await World.CreateAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => world.Service().CalculateAsync(world.ManagerAccount.Id, Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    [Theory]
    [InlineData(PerformanceEvaluationStatus.InReview)]
    [InlineData(PerformanceEvaluationStatus.Approved)]
    [InlineData(PerformanceEvaluationStatus.Disputed)]
    [InlineData(PerformanceEvaluationStatus.Final)]
    public async Task Calculate_WhenEvaluationIsPastCalculation_ThrowsConflict(PerformanceEvaluationStatus status)
    {
        using var world = await World.CreateAsync();
        world.Evaluation.Status = status;
        await world.SaveAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id));

        Assert.Equal(ErrorCodes.PerformanceEvaluationInvalidState, ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(0, await world.Db.PerformanceContributions.CountAsync());
    }

    [Theory]
    [InlineData(PeriodStatus.Locked)]
    [InlineData(PeriodStatus.Closed)]
    public async Task Calculate_WhenPeriodIsLockedOrClosed_ThrowsConflict(PeriodStatus status)
    {
        using var world = await World.CreateAsync();
        world.Period.Status = status;
        await world.SaveAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id));

        Assert.Equal(ErrorCodes.PerformanceEvaluationInvalidState, ex.ErrorCode);
    }

    [Theory]
    [InlineData(PeriodStatus.Open)]
    [InlineData(PeriodStatus.InReview)]
    public async Task Calculate_WhenPeriodIsOpenOrInReview_Succeeds(PeriodStatus status)
    {
        using var world = await World.CreateAsync();
        world.Period.Status = status;
        await world.SaveAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(world.Evaluation.Id, dto.Id);
    }

    // ----- Authorization --------------------------------------------------------------------

    [Fact]
    public async Task Calculate_WithoutPermission_ThrowsForbidden()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(scope: null).CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id));
    }

    [Fact]
    public async Task Calculate_WithOwnScope_ThrowsForbidden()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service(scope: PermissionScopes.Own).CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id));
    }

    [Fact]
    public async Task Calculate_ForEmployeeOutsideTheCallersDepartment_ThrowsForbidden()
    {
        using var world = await World.CreateAsync();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service().CalculateAsync(world.OtherDeptManagerAccount.Id, world.Evaluation.Id));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(0, await world.Db.PerformanceContributions.CountAsync());
        Assert.Equal(0, await world.Db.ActualProgresses.CountAsync());
    }

    [Fact]
    public async Task Calculate_WithBranchScope_AllowsEmployeesOfSiblingDepartments()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(scope: PermissionScopes.Branch)
            .CalculateAsync(world.OtherDeptManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(world.Evaluation.Id, dto.Id);
    }

    [Fact]
    public async Task Calculate_WithAllScope_AllowsAnyEmployee()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service(scope: PermissionScopes.All)
            .CalculateAsync(world.OtherDeptManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(world.Evaluation.Id, dto.Id);
    }

    [Fact]
    public async Task Calculate_WithDepartmentScope_AllowsEmployeesOfDescendantTeams()
    {
        using var world = await World.CreateAsync();

        var dto = await world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id);

        Assert.Equal(world.Employee.Id, dto.EmployeeId);
    }

    [Fact]
    public async Task Calculate_WithLockedAccount_ThrowsForbidden()
    {
        using var world = await World.CreateAsync();
        world.ManagerAccount.IsLocked = true;
        await world.SaveAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id));
    }

    [Fact]
    public async Task Calculate_WithInactiveEmployee_ThrowsForbidden()
    {
        using var world = await World.CreateAsync();
        world.Manager.Status = EmployeeStatus.Inactive;
        await world.SaveAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service().CalculateAsync(world.ManagerAccount.Id, world.Evaluation.Id));
    }

    [Fact]
    public async Task Calculate_WithUnknownAccount_ThrowsForbidden()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => world.Service().CalculateAsync(Guid.NewGuid(), world.Evaluation.Id));
    }

    // ----- Fixture --------------------------------------------------------------------------

    private sealed class World : IDisposable
    {
        private int _sequence;

        public required AppDbContext Db { get; init; }
        public required Department Branch { get; init; }
        public required Department DeptA { get; init; }
        public required Department DeptB { get; init; }
        public required Department TeamA1 { get; init; }
        public required Employee Employee { get; init; }
        public required Employee OtherEmployee { get; init; }
        public required Employee Manager { get; init; }
        public required Account ManagerAccount { get; init; }
        public required Account OtherDeptManagerAccount { get; init; }
        public required EvaluationPeriod Period { get; init; }
        public required Dictionary<DateOnly, Guid> DayPeriods { get; init; }
        public required KpiDefinition KpiSum { get; init; }
        public required KpiDefinition KpiRatio { get; init; }
        public required KpiDefinition KpiAvg { get; init; }
        public required PerformanceEvaluation Evaluation { get; init; }

        public static async Task<World> CreateAsync()
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
                Title = "Position",
                Status = RecordStatus.Active,
                UpdatedAt = Now,
            };
            db.JobPositions.Add(position);

            var employee = Person("E001", team, position);
            var other = Person("E002", team, position);
            var manager = Person("M001", deptA, position);
            var otherManager = Person("M002", deptB, position);
            db.Employees.AddRange(employee, other, manager, otherManager);

            var managerAccount = Login(manager);
            var otherManagerAccount = Login(otherManager);
            db.Accounts.AddRange(managerAccount, otherManagerAccount);

            var group = new KpiGroup
            {
                Id = Guid.NewGuid(),
                Code = "KD",
                Name = "Kinh doanh",
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
            db.KpiGroups.Add(group);

            var sum = Kpi("SUM", group, KpiAggregationType.Sum, KpiDirection.HigherBetter, 150m);
            var ratio = Kpi("RATIO", group, KpiAggregationType.Ratio, KpiDirection.LowerBetter, 120m);
            var avg = Kpi("AVG", group, KpiAggregationType.Avg, KpiDirection.HigherBetter, null);
            db.KpiDefinitions.AddRange(sum, ratio, avg);

            var profile = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                Code = "EP",
                Name = "Profile",
                Version = 1,
                EffectiveFrom = PeriodStart,
                Status = EvaluationProfileStatus.Active,
                UpdatedAt = Now,
            };
            db.EvaluationProfiles.Add(profile);
            db.EvaluationProfileItems.Add(new EvaluationProfileItem
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = profile.Id,
                KpiGroupId = group.Id,
                WeightPercent = 70m,
                DisplayOrder = 1,
                UpdatedAt = Now,
            });

            var period = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = "2026-10",
                Name = "Tháng 10/2026",
                PeriodType = PeriodType.Month,
                StartDate = PeriodStart,
                EndDate = PeriodEnd,
                Status = PeriodStatus.Open,
                UpdatedAt = Now,
            };
            db.EvaluationPeriods.Add(period);

            var dayPeriods = new Dictionary<DateOnly, Guid>();
            foreach (var day in new[] { 10, 11 })
            {
                var date = new DateOnly(2026, 10, day);
                var dayPeriod = new EvaluationPeriod
                {
                    Id = Guid.NewGuid(),
                    Code = $"D{date:yyyy-MM-dd}",
                    Name = $"Ngày {date:dd/MM/yyyy}",
                    PeriodType = PeriodType.Day,
                    ParentPeriodId = period.Id,
                    StartDate = date,
                    EndDate = date,
                    Status = PeriodStatus.Open,
                    UpdatedAt = Now,
                };
                db.EvaluationPeriods.Add(dayPeriod);
                dayPeriods[date] = dayPeriod.Id;
            }

            var evaluation = new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EvaluationPeriodId = period.Id,
                EvaluationProfileId = profile.Id,
                Status = PerformanceEvaluationStatus.Draft,
                UpdatedAt = Now,
            };
            db.PerformanceEvaluations.Add(evaluation);

            await db.SaveChangesAsync();

            return new World
            {
                Db = db,
                Branch = branch,
                DeptA = deptA,
                DeptB = deptB,
                TeamA1 = team,
                Employee = employee,
                OtherEmployee = other,
                Manager = manager,
                ManagerAccount = managerAccount,
                OtherDeptManagerAccount = otherManagerAccount,
                Period = period,
                DayPeriods = dayPeriods,
                KpiSum = sum,
                KpiRatio = ratio,
                KpiAvg = avg,
                Evaluation = evaluation,
            };
        }

        public PerformanceEvaluationService Service(
            IPerformanceScoreFormula? formula = null,
            string? scope = PermissionScopes.Dept) =>
            new(
                Db,
                new FakePermissions(scope),
                formula ?? new PerformanceScoreFormula(),
                new FixedTime(Now),
                NullLogger<PerformanceEvaluationService>.Instance);

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public TargetAssignment AddTarget(
            KpiDefinition kpi,
            decimal value,
            bool employeeLevel,
            int version,
            TargetAssignmentStatus status = TargetAssignmentStatus.Active,
            Guid? departmentId = null)
        {
            var target = new TargetAssignment
            {
                Id = Guid.NewGuid(),
                KpiDefinitionId = kpi.Id,
                EmployeeId = employeeLevel ? Employee.Id : null,
                DepartmentId = employeeLevel ? null : departmentId ?? TeamA1.Id,
                EvaluationPeriodId = Period.Id,
                Version = version,
                TargetValue = value,
                Status = status,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.TargetAssignments.Add(target);
            return target;
        }

        public WorkResultItem AddResult(
            KpiDefinition kpi,
            decimal value,
            decimal? denominator,
            DateTimeOffset verifiedAt,
            Employee? submitter = null,
            VerifiedResultStatus resultStatus = VerifiedResultStatus.Valid)
        {
            var owner = submitter ?? Employee;
            _sequence++;

            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = $"W-{_sequence}",
                Title = $"Work {_sequence}",
                AssigneeEmployeeId = owner.Id,
                Status = WorkStatus.Verified,
                Priority = WorkPriority.Medium,
                UpdatedAt = Now,
            };
            var submission = new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                SubmitterEmployeeId = owner.Id,
                SubmissionNo = 1,
                Status = SubmissionStatus.Approved,
                SubmittedAt = verifiedAt,
                UpdatedAt = Now,
            };
            var verification = new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = Manager.Id,
                RoundNo = 1,
                Decision = VerificationDecision.Approved,
                VerifiedAt = verifiedAt,
                UpdatedAt = Now,
            };
            var result = new VerifiedWorkResult
            {
                Id = Guid.NewGuid(),
                VerificationId = verification.Id,
                VerifiedAt = verifiedAt,
                Status = resultStatus,
                UpdatedAt = Now,
            };
            var item = new WorkResultItem
            {
                Id = Guid.NewGuid(),
                VerifiedWorkResultId = result.Id,
                KpiDefinitionId = kpi.Id,
                Value = value,
                Denominator = denominator,
                UpdatedAt = Now,
            };

            Db.Works.Add(work);
            Db.ResultSubmissions.Add(submission);
            Db.Verifications.Add(verification);
            Db.VerifiedWorkResults.Add(result);
            Db.WorkResultItems.Add(item);
            return item;
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

        private static Employee Person(string code, Department department, JobPosition position) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@example.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
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

        private static KpiDefinition Kpi(
            string code,
            KpiGroup group,
            KpiAggregationType aggregation,
            KpiDirection direction,
            decimal? max) => new()
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = code,
                Name = code,
                Unit = "unit",
                AggregationType = aggregation,
                Direction = direction,
                MaxAchievementPercent = max,
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
    }

    private sealed class FakeScoreFormula(KpiScoreResult kpiScore, decimal? groupScore, decimal? totalScore)
        : IPerformanceScoreFormula
    {
        public List<KpiScoreInput> KpiInputs { get; } = [];
        public List<GroupScoreInput> GroupInputs { get; } = [];
        public List<IReadOnlyList<GroupScoreEntry>> TotalInputs { get; } = [];

        public KpiScoreResult CalculateKpi(KpiScoreInput input)
        {
            KpiInputs.Add(input);
            return kpiScore;
        }

        public decimal? CalculateGroup(GroupScoreInput input)
        {
            GroupInputs.Add(input);
            return groupScore;
        }

        public decimal? CalculateTotal(IReadOnlyList<GroupScoreEntry> groups)
        {
            TotalInputs.Add(groups);
            return totalScore;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePermissions(string? scope) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = scope is null
                ? []
                : [new UserPermissionDto("evaluation", "calculate", scope, "EVALUATION_CALCULATE")];
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
