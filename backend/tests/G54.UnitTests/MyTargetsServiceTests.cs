using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class MyTargetsServiceTests
{
    private sealed record Fixture(
        AppDbContext Db,
        MyTargetsService Service,
        Guid EmployeeId,
        Guid ManagerId,
        Guid DepartmentId,
        Guid KpiId,
        int Year,
        Guid YearRowId,
        Guid TargetAssignmentFeedbackOwnerId);

    private static async Task<Fixture> CreateAsync(KpiAggregationType aggregation = KpiAggregationType.Sum)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        var department = new Department { Id = Guid.NewGuid(), Code = "DEPT", Name = "Dept", LevelType = DepartmentLevelType.Department };
        var jobPosition = new JobPosition { Id = Guid.NewGuid(), Code = "JP", Title = "JP" };
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E1",
            FullName = "Employee One",
            Email = "e1@test.local",
            DepartmentId = department.Id,
            JobPositionId = jobPosition.Id,
            Status = EmployeeStatus.Active
        };
        var manager = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "M1",
            FullName = "Manager One",
            Email = "m1@test.local",
            DepartmentId = department.Id,
            JobPositionId = jobPosition.Id,
            Status = EmployeeStatus.Active
        };

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group" };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI_1",
            Name = "KPI One",
            Unit = "VND",
            AggregationType = aggregation,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        };

        var year = 2027;
        var yearPeriod = new EvaluationPeriod { Id = Guid.NewGuid(), Code = $"{year}", Name = $"Năm {year}", PeriodType = PeriodType.Year, StartDate = new DateOnly(year, 1, 1), EndDate = new DateOnly(year, 12, 31), Status = PeriodStatus.Open };
        var q1 = new EvaluationPeriod { Id = Guid.NewGuid(), Code = $"{year}-Q1", Name = "Q1", PeriodType = PeriodType.Quarter, ParentPeriodId = yearPeriod.Id, StartDate = new DateOnly(year, 1, 1), EndDate = new DateOnly(year, 3, 31), Status = PeriodStatus.Open };
        var q2 = new EvaluationPeriod { Id = Guid.NewGuid(), Code = $"{year}-Q2", Name = "Q2", PeriodType = PeriodType.Quarter, ParentPeriodId = yearPeriod.Id, StartDate = new DateOnly(year, 4, 1), EndDate = new DateOnly(year, 6, 30), Status = PeriodStatus.Open };
        var q3 = new EvaluationPeriod { Id = Guid.NewGuid(), Code = $"{year}-Q3", Name = "Q3", PeriodType = PeriodType.Quarter, ParentPeriodId = yearPeriod.Id, StartDate = new DateOnly(year, 7, 1), EndDate = new DateOnly(year, 9, 30), Status = PeriodStatus.Open };
        var q4 = new EvaluationPeriod { Id = Guid.NewGuid(), Code = $"{year}-Q4", Name = "Q4", PeriodType = PeriodType.Quarter, ParentPeriodId = yearPeriod.Id, StartDate = new DateOnly(year, 10, 1), EndDate = new DateOnly(year, 12, 31), Status = PeriodStatus.Open };

        var yearRow = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = employee.Id,
            EvaluationPeriodId = yearPeriod.Id,
            SplitDimension = TargetSplitDimension.Org,
            TargetValue = 100m,
            Status = TargetAssignmentStatus.Active,
            ApprovedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = yearPeriod.StartDate
        };
        var quarterRows = new[] { q1, q2, q3, q4 }.Select((q, i) => new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = employee.Id,
            EvaluationPeriodId = q.Id,
            ParentAssignmentId = yearRow.Id,
            SplitDimension = TargetSplitDimension.Period,
            TargetValue = 25m,
            Status = TargetAssignmentStatus.Active,
            ApprovedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = q.StartDate
        }).ToList();

        db.Departments.Add(department);
        db.JobPositions.Add(jobPosition);
        db.Employees.AddRange(employee, manager);
        db.KpiGroups.Add(group);
        db.KpiDefinitions.Add(kpi);
        db.EvaluationPeriods.AddRange(yearPeriod, q1, q2, q3, q4);
        db.TargetAssignments.Add(yearRow);
        db.TargetAssignments.AddRange(quarterRows);
        await db.SaveChangesAsync();

        var uow = new UnitOfWork(db);
        var permissions = new AllocationPermissions(admin: true);
        var changeLog = new TargetChangeLogService(db, TimeProvider.System);
        var service = new MyTargetsService(uow, permissions, changeLog, TimeProvider.System, Options.Create(new SystemParametersOptions()));

        return new Fixture(db, service, employee.Id, manager.Id, department.Id, kpi.Id, year, yearRow.Id, employee.Id);
    }

    [Fact]
    public async Task GetMyTargets_ReturnsYearAndQuarters()
    {
        var f = await CreateAsync();

        var result = await f.Service.GetMyTargetsAsync(f.EmployeeId, f.Year, CancellationToken.None);

        Assert.Equal(f.Year, result.Year);
        Assert.Single(result.Kpis);
        var row = result.Kpis[0];
        Assert.Equal(100m, row.Year);
        Assert.Equal(25m, row.Q1);
        Assert.Equal(25m, row.Q4);
        Assert.False(row.Changed);
        Assert.Equal(EmployeeTargetAckStatus.Pending, result.AckStatus);
    }

    [Fact]
    public async Task GetMyTargets_ReflectsChangeLogAsChanged()
    {
        var f = await CreateAsync();
        await f.Service.GetProgressAsync(f.EmployeeId, f.Year, null, CancellationToken.None); // warm path, no-op assertion
        var changeLog = new TargetChangeLogService(f.Db, TimeProvider.System);
        await changeLog.LogAsync(
            f.ManagerId, TargetChangeLogAction.Adjust, tier: 3, kpiId: f.KpiId, unit: "VND", period: "YEAR",
            oldValue: 100m, newValue: 120m, basis: "MANUAL", employeeId: f.EmployeeId, departmentId: f.DepartmentId, year: f.Year);

        var result = await f.Service.GetMyTargetsAsync(f.EmployeeId, f.Year, CancellationToken.None);

        var row = Assert.Single(result.Kpis);
        Assert.True(row.Changed);
        Assert.Equal(100m, row.ChangedOldValue);
        Assert.Equal(120m, row.ChangedNewValue);
    }

    [Fact]
    public async Task Confirm_FirstTime_SetsConfirmed()
    {
        var f = await CreateAsync();

        var result = await f.Service.ConfirmAsync(f.EmployeeId, f.Year, CancellationToken.None);

        Assert.Equal(EmployeeTargetAckStatus.Confirmed, result.AckStatus);
        var ack = await f.Db.EmployeeTargetAcks.AsNoTracking().SingleAsync(a => a.EmployeeId == f.EmployeeId && a.Year == f.Year);
        Assert.Equal(EmployeeTargetAckStatus.Confirmed, ack.Status);
        Assert.NotNull(ack.ConfirmedAt);
    }

    [Fact]
    public async Task Confirm_AlreadyConfirmed_ThrowsConflict()
    {
        var f = await CreateAsync();
        await f.Service.ConfirmAsync(f.EmployeeId, f.Year, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => f.Service.ConfirmAsync(f.EmployeeId, f.Year, CancellationToken.None));

        Assert.Equal(ErrorCodes.TargetAckInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task SubmitFeedback_ReasonTooLong_ThrowsMaxLengthExceeded()
    {
        var f = await CreateAsync();
        var request = new SubmitTargetFeedbackRequest(f.KpiId, new string('x', 501), null, null, null, null, null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.SubmitFeedbackAsync(f.EmployeeId, f.Year, request, CancellationToken.None));

        Assert.Equal(ErrorCodes.MaxLengthExceeded, ex.ErrorCode);
    }

    [Fact]
    public async Task SubmitFeedback_CumulativeProposalSumMismatch_Throws422()
    {
        var f = await CreateAsync(KpiAggregationType.Sum);
        var request = new SubmitTargetFeedbackRequest(f.KpiId, "Đề xuất tăng", 120m, 25m, 25m, 25m, 25m); // sums to 100, not 120

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.SubmitFeedbackAsync(f.EmployeeId, f.Year, request, CancellationToken.None));

        Assert.Equal(ErrorCodes.TargetFeedbackProposedSumMismatch, ex.ErrorCode);
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_Valid_PersistsAndLogsChange()
    {
        var f = await CreateAsync();
        var request = new SubmitTargetFeedbackRequest(f.KpiId, "Đề xuất tăng chỉ tiêu quý 4", 120m, 25m, 25m, 25m, 45m);

        var result = await f.Service.SubmitFeedbackAsync(f.EmployeeId, f.Year, request, CancellationToken.None);

        Assert.Equal(120m, result.ProposedYear);
        Assert.Equal("Đề xuất tăng chỉ tiêu quý 4", result.Reason);
        Assert.Null(result.Reply);

        var feedback = await f.Db.TargetAssignmentFeedbacks.AsNoTracking().SingleAsync();
        Assert.Equal(f.EmployeeId, feedback.EmployeeId);

        var changeLog = new TargetChangeLogService(f.Db, TimeProvider.System);
        var logs = await changeLog.GetByEmployeeYearAsync(f.EmployeeId, f.Year, CancellationToken.None);
        Assert.Contains(logs, l => l.Action == TargetChangeLogAction.Feedback);
    }

    [Fact]
    public async Task ReplyFeedback_OutOfScopeManager_ThrowsForbidden()
    {
        var f = await CreateAsync();
        var request = new SubmitTargetFeedbackRequest(f.KpiId, "Đề xuất tăng", null, null, null, null, null);
        var feedback = await f.Service.SubmitFeedbackAsync(f.EmployeeId, f.Year, request, CancellationToken.None);

        var restrictedUow = new UnitOfWork(f.Db);
        var restrictedPermissions = new AllocationPermissions(admin: false);
        var restrictedChangeLog = new TargetChangeLogService(f.Db, TimeProvider.System);
        var restrictedService = new MyTargetsService(restrictedUow, restrictedPermissions, restrictedChangeLog, TimeProvider.System, Options.Create(new SystemParametersOptions()));
        var outsiderId = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenException>(() => restrictedService.ReplyFeedbackAsync(
            outsiderId, feedback.Id, new ReplyTargetFeedbackRequest("Đã ghi nhận."), CancellationToken.None));
    }

    [Fact]
    public async Task GetChangeLog_Self_WithoutManagePermission_ReturnsEntries()
    {
        var f = await CreateAsync();
        await new TargetChangeLogService(f.Db, TimeProvider.System).LogAsync(
            f.ManagerId, TargetChangeLogAction.Adjust, tier: 3, kpiId: f.KpiId, unit: "VND", period: "YEAR",
            oldValue: 100m, newValue: 120m, basis: "MANUAL", employeeId: f.EmployeeId, departmentId: f.DepartmentId, year: f.Year);
        var employeeService = new MyTargetsService(new UnitOfWork(f.Db), new AllocationPermissions(admin: false),
            new TargetChangeLogService(f.Db, TimeProvider.System), TimeProvider.System, Options.Create(new SystemParametersOptions()));

        var result = await employeeService.GetChangeLogAsync(f.EmployeeId, f.Year, f.EmployeeId, null, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetChangeLog_SameDepartmentColleagueWithoutManagePermission_ThrowsForbidden()
    {
        var f = await CreateAsync();
        var colleagueService = new MyTargetsService(new UnitOfWork(f.Db), new AllocationPermissions(admin: false),
            new TargetChangeLogService(f.Db, TimeProvider.System), TimeProvider.System, Options.Create(new SystemParametersOptions()));

        await Assert.ThrowsAsync<ForbiddenException>(() => colleagueService.GetChangeLogAsync(
            f.ManagerId, f.Year, f.EmployeeId, null, CancellationToken.None));
    }

    [Fact]
    public async Task ReplyFeedback_InScope_SetsReply()
    {
        var f = await CreateAsync();
        var request = new SubmitTargetFeedbackRequest(f.KpiId, "Đề xuất tăng", null, null, null, null, null);
        var feedback = await f.Service.SubmitFeedbackAsync(f.EmployeeId, f.Year, request, CancellationToken.None);

        var result = await f.Service.ReplyFeedbackAsync(
            f.ManagerId, feedback.Id, new ReplyTargetFeedbackRequest("Đã ghi nhận, sẽ xem xét."), CancellationToken.None);

        Assert.Equal("Đã ghi nhận, sẽ xem xét.", result.Reply);
        Assert.Equal(f.ManagerId, result.RepliedBy);
        Assert.NotNull(result.RepliedAt);
    }

    [Fact]
    public async Task GetProgress_Year_ComputesAchievedAndPace()
    {
        var f = await CreateAsync();
        f.Db.ActualProgresses.Add(new ActualProgress
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = f.YearRowId,
            ProgressDate = new DateOnly(f.Year, 2, 1),
            Value = 40m,
            Status = ActualProgressStatus.Valid,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetProgressAsync(f.EmployeeId, f.Year, "YEAR", CancellationToken.None);

        var row = Assert.Single(result.Kpis);
        Assert.Equal(40m, row.Progress.Achieved);
        Assert.Equal(100m, row.Progress.Target);
        Assert.Equal(0, row.Progress.InReviewCount);
    }
}
