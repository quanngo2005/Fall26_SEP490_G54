using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace G54.UnitTests;

public sealed class TargetAllocationServiceTests
{
    private sealed record Fixture(AppDbContext Db, TargetAllocationService Service, Guid BranchId, Guid DeptId,
        Guid KpiId, Guid YearPeriodId, Guid Q1Id, TargetAssignment BranchYearTarget);

    private static async Task<Fixture> CreateAsync(PeriodStatus status = PeriodStatus.Open)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        var branch = new Department { Id = Guid.NewGuid(), Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch };
        var dept = new Department { Id = Guid.NewGuid(), Code = "D1", Name = "Dept", LevelType = DepartmentLevelType.Department, ParentDepartmentId = branch.Id };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "G" };
        var kpi = new KpiDefinition { Id = Guid.NewGuid(), KpiGroupId = group.Id, Code = "K1", Name = "Open accounts", Unit = "count" };
        var year = new EvaluationPeriod { Id = Guid.NewGuid(), Code = "2026", Name = "2026", PeriodType = PeriodType.Year, StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Status = status };
        var q1 = new EvaluationPeriod { Id = Guid.NewGuid(), Code = "2026-Q1", Name = "Q1", PeriodType = PeriodType.Quarter, StartDate = new(2026, 1, 1), EndDate = new(2026, 3, 31), Status = status };
        var months = Enumerable.Range(1, 3).Select(m => new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"2026-{m:00}",
            Name = $"M{m}",
            PeriodType = PeriodType.Month,
            StartDate = new(2026, m, 1),
            EndDate = new DateOnly(2026, m, 1).AddMonths(1).AddDays(-1),
            Status = status,
        }).ToList();
        var days = Enumerable.Range(0, 90).Select(i => new DateOnly(2026, 1, 1).AddDays(i)).Select(d => new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "D" + d.DayNumber,
            Name = "day",
            PeriodType = PeriodType.Day,
            StartDate = d,
            EndDate = d,
            Status = status,
        }).ToList();
        var branchYearTarget = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = branch.Id,
            KpiDefinitionId = kpi.Id,
            EvaluationPeriodId = year.Id,
            TargetValue = 1000,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
        };

        db.Departments.AddRange(branch, dept);
        db.KpiGroups.Add(group);
        db.KpiDefinitions.Add(kpi);
        db.EvaluationPeriods.AddRange(year, q1);
        db.EvaluationPeriods.AddRange(months);
        db.EvaluationPeriods.AddRange(days);
        db.TargetAssignments.Add(branchYearTarget);
        await db.SaveChangesAsync();

        return new Fixture(db, new TargetAllocationService(new UnitOfWork(db)), branch.Id, dept.Id, kpi.Id, year.Id, q1.Id, branchYearTarget);
    }

    [Fact]
    public void SplitByWorkingDays_SumsExactlyToTotal()
    {
        var parts = new List<(DateOnly, DateOnly)>
        {
            (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)),
        };

        var result = TargetAllocationService.SplitByWorkingDays(1000m, parts);

        Assert.Equal(3, result.Count);
        Assert.Equal(1000m, result.Sum());
    }

    [Fact]
    public void SplitByWorkingDays_WeekendOnlyPartGetsZero()
    {
        var parts = new List<(DateOnly, DateOnly)>
        {
            (new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9)),
            (new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 11)),
        };

        var result = TargetAllocationService.SplitByWorkingDays(100m, parts);

        Assert.Equal(100m, result[0]);
        Assert.Equal(0m, result[1]);
    }

    [Fact]
    public async Task AllocateDepartments_SumBelowTarget_Throws422()
    {
        var f = await CreateAsync();
        var dto = new AllocateDepartmentsDto([new(f.DeptId, f.KpiId, 900m)], null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, dto, Guid.NewGuid()));

        Assert.Equal(ErrorCodes.TargetDepartmentSumBelow, ex.ErrorCode);
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task AllocateDepartments_SumAboveTarget_Succeeds()
    {
        var f = await CreateAsync();
        var dto = new AllocateDepartmentsDto([new(f.DeptId, f.KpiId, 1200m)], null);

        await f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, dto, Guid.NewGuid());

        var deptYearTarget = await f.Db.TargetAssignments.SingleAsync(t => t.ParentAssignmentId == f.BranchYearTarget.Id);
        Assert.Equal(1200m, deptYearTarget.TargetValue);
        Assert.Equal(TargetAssignmentStatus.Draft, deptYearTarget.Status);
    }

    [Fact]
    public async Task AllocateDepartments_DepartmentOutsideBranch_Throws()
    {
        var f = await CreateAsync();
        var other = new Department { Id = Guid.NewGuid(), Code = "X", Name = "X", LevelType = DepartmentLevelType.Department };
        f.Db.Departments.Add(other);
        await f.Db.SaveChangesAsync();
        var dto = new AllocateDepartmentsDto([new(other.Id, f.KpiId, 1000m)], null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, dto, Guid.NewGuid()));

        Assert.Equal(ErrorCodes.TargetDepartmentNotInBranch, ex.ErrorCode);
    }

    [Fact]
    public async Task AllocateDepartments_PeriodNotOpen_ThrowsConflict()
    {
        var f = await CreateAsync(PeriodStatus.Locked);
        var dto = new AllocateDepartmentsDto([new(f.DeptId, f.KpiId, 1000m)], null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, dto, Guid.NewGuid()));

        Assert.Equal(ErrorCodes.TargetPeriodNotOpen, ex.ErrorCode);
    }

    [Fact]
    public async Task AllocateQuarters_SumBelowTarget_Throws422()
    {
        var f = await CreateAsync();
        await f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, new([new(f.DeptId, f.KpiId, 1000m)], null), Guid.NewGuid());
        var deptYearTarget = await f.Db.TargetAssignments.SingleAsync(t => t.ParentAssignmentId == f.BranchYearTarget.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.AllocateQuartersAsync(deptYearTarget.Id, new([new(f.Q1Id, f.KpiId, 100m)], null), Guid.NewGuid()));

        Assert.Equal(ErrorCodes.TargetQuarterSumBelow, ex.ErrorCode);
    }

    [Fact]
    public async Task Confirm_ActivatesAndSplitsMonthsAndDays_SumsMatch()
    {
        var f = await CreateAsync();
        await f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, new([new(f.DeptId, f.KpiId, 1000m)], null), Guid.NewGuid());
        var deptYearTarget = await f.Db.TargetAssignments.SingleAsync(t => t.ParentAssignmentId == f.BranchYearTarget.Id);
        await f.Service.AllocateQuartersAsync(deptYearTarget.Id, new([new(f.Q1Id, f.KpiId, 1000m)], null), Guid.NewGuid());

        await f.Service.ConfirmAsync(deptYearTarget.Id, Guid.NewGuid());

        var quarterTarget = await f.Db.TargetAssignments.SingleAsync(t => t.ParentAssignmentId == deptYearTarget.Id);
        Assert.Equal(TargetAssignmentStatus.Active, quarterTarget.Status);
        var monthTargets = await f.Db.TargetAssignments.Where(t => t.ParentAssignmentId == quarterTarget.Id).ToListAsync();
        Assert.Equal(3, monthTargets.Count);
        Assert.Equal(1000m, monthTargets.Sum(t => t.TargetValue));
        var monthTargetIds = monthTargets.Select(t => t.Id).ToList();
        var days = await f.Db.TargetAssignments.Where(t => t.ParentAssignmentId != null && monthTargetIds.Contains(t.ParentAssignmentId.Value)).ToListAsync();
        Assert.Equal(1000m, days.Sum(t => t.TargetValue));
    }

    [Fact]
    public async Task Reallocate_AfterConfirm_SupersedesOldAndBumpsVersion()
    {
        var f = await CreateAsync();
        await f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, new([new(f.DeptId, f.KpiId, 1000m)], null), Guid.NewGuid());
        var deptYearTarget = await f.Db.TargetAssignments.SingleAsync(t => t.ParentAssignmentId == f.BranchYearTarget.Id);
        await f.Service.AllocateQuartersAsync(deptYearTarget.Id, new([new(f.Q1Id, f.KpiId, 1000m)], null), Guid.NewGuid());
        await f.Service.ConfirmAsync(deptYearTarget.Id, Guid.NewGuid());

        await f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, new([new(f.DeptId, f.KpiId, 1100m)], "mid-year change"), Guid.NewGuid());

        var rows = await f.Db.TargetAssignments.Where(t => t.ParentAssignmentId == f.BranchYearTarget.Id).ToListAsync();
        Assert.Contains(rows, r => r.Status == TargetAssignmentStatus.Superseded && r.Version == 1);
        Assert.Contains(rows, r => r.Version == 2 && r.TargetValue == 1100m);
    }

    [Fact]
    public async Task Reconfirm_AfterQuarterChange_CreatesNextVersionOfMonthRows()
    {
        var f = await CreateAsync();
        await f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, new([new(f.DeptId, f.KpiId, 1000m)], null), Guid.NewGuid());
        var deptYearTarget = await f.Db.TargetAssignments.SingleAsync(t => t.ParentAssignmentId == f.BranchYearTarget.Id);
        await f.Service.AllocateQuartersAsync(deptYearTarget.Id, new([new(f.Q1Id, f.KpiId, 1000m)], null), Guid.NewGuid());
        await f.Service.ConfirmAsync(deptYearTarget.Id, Guid.NewGuid());
        await f.Service.AllocateQuartersAsync(deptYearTarget.Id, new([new(f.Q1Id, f.KpiId, 1000m)], "same total, new version"), Guid.NewGuid());

        await f.Service.ConfirmAsync(deptYearTarget.Id, Guid.NewGuid());

        var months = await f.Db.TargetAssignments
            .Where(t => t.EvaluationPeriod.PeriodType == PeriodType.Month).ToListAsync();
        Assert.Equal(3, months.Count(m => m.Status == TargetAssignmentStatus.Active && m.Version == 2));
        Assert.Equal(3, months.Count(m => m.Status == TargetAssignmentStatus.Superseded && m.Version == 1));
    }

    [Fact]
    public async Task AllocateQuarters_ActiveRowWithoutChangeReason_ThrowsValidation()
    {
        var f = await CreateAsync();
        await f.Service.AllocateDepartmentsAsync(f.BranchYearTarget.Id, new([new(f.DeptId, f.KpiId, 1000m)], null), Guid.NewGuid());
        var deptYearTarget = await f.Db.TargetAssignments.SingleAsync(t => t.ParentAssignmentId == f.BranchYearTarget.Id);
        await f.Service.AllocateQuartersAsync(deptYearTarget.Id, new([new(f.Q1Id, f.KpiId, 1000m)], null), Guid.NewGuid());
        await f.Service.ConfirmAsync(deptYearTarget.Id, Guid.NewGuid());

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service.AllocateQuartersAsync(deptYearTarget.Id, new([new(f.Q1Id, f.KpiId, 1000m)], null), Guid.NewGuid()));
    }
}
