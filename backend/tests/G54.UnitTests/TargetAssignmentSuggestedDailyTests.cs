using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class TargetAssignmentSuggestedDailyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private Department _dept = null!;
    private Department _otherDept = null!;
    private Employee _member = null!;
    private Employee _peer = null!;
    private Employee _outsider = null!;
    private Guid _admin;
    private Guid _manager;
    private Guid _memberAccount;
    private Guid _peerAccount;
    private Guid _outsiderManager;
    private EvaluationPeriod _quarter = null!;
    private KpiGroup _group = null!;

    public void Dispose() => _db.Dispose();

    private TargetAssignmentService Service(bool admin = false) => new(
        new TargetAssignmentRepository(_db),
        new TargetAssignmentFeedbackRepository(_db),
        new Repository<EvaluationPeriod>(_db),
        new FakePermissions(admin, _manager, _outsiderManager),
        new WeekdayWorkingDayCalendar(),
        new RecordingNotifier(),
        new FixedTime());

    /// <summary>Q4/2026 has 22 + 21 + 23 = 66 weekdays. DEPT holds the member, a peer and the manager; OTHER holds an outsider.</summary>
    private async Task SeedAsync()
    {
        _dept = Department("D");
        _otherDept = Department("O");
        _db.Departments.AddRange(_dept, _otherDept);

        var manager = Employee("MGR", _dept.Id);
        _member = Employee("MEM", _dept.Id);
        _peer = Employee("PEER", _dept.Id);
        _outsider = Employee("OUT", _otherDept.Id);
        var admin = Employee("ADM", _dept.Id);
        var outsiderManager = Employee("OMGR", _otherDept.Id);
        _db.Employees.AddRange(manager, _member, _peer, _outsider, admin, outsiderManager);
        _manager = AddAccount(manager);
        _memberAccount = AddAccount(_member);
        _peerAccount = AddAccount(_peer);
        _admin = AddAccount(admin);
        _outsiderManager = AddAccount(outsiderManager);

        _quarter = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-Q4",
            Name = "Q4",
            PeriodType = PeriodType.Quarter,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "G", Status = KpiStatus.Active, UpdatedAt = Now };
        _db.EvaluationPeriods.Add(_quarter);
        _db.KpiGroups.Add(_group);
        await _db.SaveChangesAsync();
    }

    private static Department Department(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        LevelType = DepartmentLevelType.Department,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static Employee Employee(string code, Guid departmentId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = code,
        Email = $"{code.ToLowerInvariant()}@x.local",
        DepartmentId = departmentId,
        JobPositionId = Guid.NewGuid(),
        Status = EmployeeStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private Guid AddAccount(Employee employee)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = employee.EmployeeCode,
            PasswordHash = "h",
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Accounts.Add(account);
        return account.Id;
    }

    private KpiDefinition AddKpi(
        string code,
        string unit,
        KpiAggregationType aggregation = KpiAggregationType.Sum,
        KpiDirection direction = KpiDirection.HigherBetter)
    {
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = _group.Id,
            Code = code,
            Name = code,
            Unit = unit,
            AggregationType = aggregation,
            Direction = direction,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };
        _db.KpiDefinitions.Add(kpi);
        return kpi;
    }

    private TargetAssignment AddTarget(
        Employee owner,
        KpiDefinition kpi,
        decimal value,
        TargetAssignmentStatus status = TargetAssignmentStatus.Active,
        EvaluationPeriod? period = null)
    {
        var target = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EvaluationPeriodId = (period ?? _quarter).Id,
            EmployeeId = owner.Id,
            Version = 1,
            TargetValue = value,
            Status = status,
            ApprovedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        _db.TargetAssignments.Add(target);
        return target;
    }

    [Fact]
    public async Task ASumKpi_ReturnsTheMonthsTheirDailyAndWeeklyShareAndTheDayCount()
    {
        await SeedAsync();
        var target = AddTarget(_member, AddKpi("HDV", "VNĐ"), 6600m);
        await _db.SaveChangesAsync();

        var result = await Service(admin: true).GetSuggestedDailyAsync(_admin, _member.Id, _quarter.Id);

        Assert.Equal(_member.Id, result.EmployeeId);
        Assert.Equal(66, result.WorkingDays);
        Assert.Equal(new DateOnly(2026, 10, 1), result.PeriodStart);
        var item = Assert.Single(result.Items);
        Assert.Equal(target.Id, item.TargetAssignmentId);
        Assert.Equal(6600m, item.TargetValue);
        Assert.Equal(100m, item.PlannedDaily);
        Assert.Equal(500m, item.PlannedWeekly);
        Assert.Equal(["2026-10", "2026-11", "2026-12"], item.Months.Select(m => m.Month));
        Assert.Equal([22, 21, 23], item.Months.Select(m => m.WorkingDays));
        Assert.Equal([2200m, 2100m, 2300m], item.Months.Select(m => m.Value));
        Assert.All(item.Months, m =>
        {
            Assert.Equal(100m, m.Daily);
            Assert.Equal(500m, m.Weekly);
        });
    }

    [Fact]
    public async Task TheMonthsAddUpToTheTarget_AndMatchTheBreakdownEndpointsMonthSplit()
    {
        await SeedAsync();
        var target = AddTarget(_member, AddKpi("HDV", "VNĐ"), 1000m);
        await _db.SaveChangesAsync();
        var service = Service(admin: true);

        var suggested = Assert.Single((await service.GetSuggestedDailyAsync(_admin, _member.Id, _quarter.Id)).Items);
        var breakdown = await service.GetBreakdownAsync(_admin, target.Id, "MONTH");

        Assert.Equal(1000m, suggested.Months.Sum(m => m.Value));
        Assert.Equal(breakdown.Items.Select(i => i.Value), suggested.Months.Select(m => m.Value));
        Assert.Equal(breakdown.Items.Select(i => i.WorkingDays), suggested.Months.Select(m => m.WorkingDays));
    }

    [Fact]
    public async Task AWholeNumberKpi_SplitsInWholeUnitsAndSaysSo()
    {
        await SeedAsync();
        AddTarget(_member, AddKpi("THE", "thẻ"), 0m);
        AddTarget(_member, AddKpi("KH", "Khách hàng"), 30m);
        await _db.SaveChangesAsync();

        var items = (await Service(admin: true).GetSuggestedDailyAsync(_admin, _member.Id, _quarter.Id)).Items;

        var customers = items.Single(i => i.KpiCode == "KH");
        Assert.True(customers.IsWholeNumber);
        Assert.Equal(30m, customers.Months.Sum(m => m.Value));
        Assert.All(customers.Months, m => Assert.Equal(m.Value, decimal.Truncate(m.Value)));
        Assert.False(items.Single(i => i.KpiCode == "THE").IsWholeNumber);
    }

    [Theory]
    [InlineData(KpiAggregationType.Avg, KpiDirection.HigherBetter)]
    [InlineData(KpiAggregationType.Ratio, KpiDirection.HigherBetter)]
    [InlineData(KpiAggregationType.NoAggregation, KpiDirection.HigherBetter)]
    [InlineData(KpiAggregationType.Sum, KpiDirection.LowerBetter)]
    [InlineData(KpiAggregationType.Sum, KpiDirection.OnTarget)]
    public async Task AKpiThatIsNotASumWhereMoreIsBetter_KeepsItsTargetAndHasNoPerDayFigures(
        KpiAggregationType aggregation, KpiDirection direction)
    {
        await SeedAsync();
        AddTarget(_member, AddKpi("CSAT", "điểm", aggregation, direction), 4.5m);
        await _db.SaveChangesAsync();

        var item = Assert.Single((await Service(admin: true).GetSuggestedDailyAsync(_admin, _member.Id, _quarter.Id)).Items);

        Assert.Equal(4.5m, item.TargetValue);
        Assert.Null(item.PlannedDaily);
        Assert.Null(item.PlannedWeekly);
        Assert.Empty(item.Months);
    }

    [Fact]
    public async Task OnlyActiveTargetsOfThatEmployeeInThatPeriodAreReturned_OrderedByKpiCode()
    {
        await SeedAsync();
        var otherPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-Q3",
            Name = "Q3",
            PeriodType = PeriodType.Quarter,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 9, 30),
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(otherPeriod);
        AddTarget(_member, AddKpi("ZED", "VNĐ"), 10m);
        AddTarget(_member, AddKpi("ABC", "VNĐ"), 20m);
        AddTarget(_member, AddKpi("DRAFT", "VNĐ"), 30m, TargetAssignmentStatus.Draft);
        AddTarget(_member, AddKpi("OLD", "VNĐ"), 40m, TargetAssignmentStatus.Superseded);
        AddTarget(_member, AddKpi("Q3", "VNĐ"), 50m, period: otherPeriod);
        AddTarget(_peer, AddKpi("PEER", "VNĐ"), 60m);
        await _db.SaveChangesAsync();

        var result = await Service(admin: true).GetSuggestedDailyAsync(_admin, _member.Id, _quarter.Id);

        Assert.Equal(["ABC", "ZED"], result.Items.Select(i => i.KpiCode));
    }

    [Fact]
    public async Task AnEmployeeWithoutTargets_GetsAnEmptyListNotAnError()
    {
        await SeedAsync();

        var result = await Service(admin: true).GetSuggestedDailyAsync(_admin, _member.Id, _quarter.Id);

        Assert.Empty(result.Items);
        Assert.Equal(66, result.WorkingDays);
    }

    [Fact]
    public async Task AnUnknownEmployee_IsNotFound()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => Service(admin: true).GetSuggestedDailyAsync(_admin, Guid.NewGuid(), _quarter.Id));
    }

    [Fact]
    public async Task AnUnknownPeriod_IsNotFound()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => Service(admin: true).GetSuggestedDailyAsync(_admin, _member.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task TheEmployeeThemselves_CanRead()
    {
        await SeedAsync();
        AddTarget(_member, AddKpi("HDV", "VNĐ"), 660m);
        await _db.SaveChangesAsync();

        var result = await Service().GetSuggestedDailyAsync(_memberAccount, _member.Id, _quarter.Id);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task AManagerOverTheEmployeesDepartment_CanRead()
    {
        await SeedAsync();
        AddTarget(_member, AddKpi("HDV", "VNĐ"), 660m);
        await _db.SaveChangesAsync();

        var result = await Service().GetSuggestedDailyAsync(_manager, _member.Id, _quarter.Id);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task APeerInTheSameDepartment_IsForbidden()
    {
        await SeedAsync();
        AddTarget(_member, AddKpi("HDV", "VNĐ"), 660m);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().GetSuggestedDailyAsync(_peerAccount, _member.Id, _quarter.Id));
    }

    [Fact]
    public async Task AManagerOfAnotherDepartment_IsForbidden()
    {
        await SeedAsync();
        AddTarget(_member, AddKpi("HDV", "VNĐ"), 660m);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service().GetSuggestedDailyAsync(_outsiderManager, _member.Id, _quarter.Id));
    }

    [Fact]
    public async Task ALockedPeriod_StillReadsBecauseThisEndpointOnlyReads()
    {
        await SeedAsync();
        _quarter.Status = PeriodStatus.Locked;
        AddTarget(_member, AddKpi("HDV", "VNĐ"), 660m);
        await _db.SaveChangesAsync();

        var result = await Service(admin: true).GetSuggestedDailyAsync(_admin, _member.Id, _quarter.Id);

        Assert.Single(result.Items);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Everyone has target:manage scope All when <paramref name="admin"/>; the listed accounts have scope Dept.</summary>
    private sealed class FakePermissions(bool admin, params Guid[] managers) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(resource == "target" && action == "manage" &&
                ((admin && requiredScope == "All") || (managers.Contains(id) && requiredScope == "Dept")));

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
