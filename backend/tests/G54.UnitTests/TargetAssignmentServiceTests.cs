using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class TargetAssignmentServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private Department _branch = null!;
    private Department _dept = null!;
    private Department _deptOther = null!;
    private Employee _member = null!;
    private Guid _admin;
    private Guid _manager;
    private Guid _kpiId;
    private Guid _periodId;

    public void Dispose() => _db.Dispose();

    private TargetAssignmentService Service(bool admin) => new(
        new TargetAssignmentRepository(_db),
        new Repository<EvaluationPeriod>(_db),
        new FakePermissions(admin),
        new FixedTime());

    /// <summary>BRANCH > (DEPT, DEPT_OTHER). The manager and the member belong to DEPT.</summary>
    private async Task SeedAsync()
    {
        _branch = Department("B", DepartmentLevelType.Branch, null);
        _dept = Department("D", DepartmentLevelType.Department, _branch.Id);
        _deptOther = Department("D2", DepartmentLevelType.Department, _branch.Id);
        _db.Departments.AddRange(_branch, _dept, _deptOther);

        var manager = Employee("MGR", _dept.Id);
        _member = Employee("MEM", _dept.Id);
        var admin = Employee("ADM", _branch.Id);
        _db.Employees.AddRange(manager, _member, admin);
        _manager = AddAccount(manager).Id;
        _admin = AddAccount(admin).Id;

        _kpiId = Guid.NewGuid();
        _periodId = Guid.NewGuid();
        await _db.SaveChangesAsync();
    }

    private static Department Department(string code, DepartmentLevelType level, Guid? parent) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        LevelType = level,
        ParentDepartmentId = parent,
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

    private Account AddAccount(Employee employee)
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
        return account;
    }

    private CreateTargetAssignmentDto Root(decimal value = 1000) => new()
    {
        KpiDefinitionId = _kpiId,
        EvaluationPeriodId = _periodId,
        DepartmentId = _branch.Id,
        TargetValue = value,
        ChangeReason = "Q4",
    };

    private async Task<TargetAssignment> StoreDepartmentTargetAsync(Department owner)
    {
        var target = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = _kpiId,
            EvaluationPeriodId = _periodId,
            DepartmentId = owner.Id,
            Version = 1,
            TargetValue = 600,
            Status = TargetAssignmentStatus.Active,
            ApprovedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        _db.TargetAssignments.Add(target);
        await _db.SaveChangesAsync();
        return target;
    }

    [Fact]
    public async Task CreateRoot_StoresAnActiveApprovedTargetForTheBranch()
    {
        await SeedAsync();

        var result = await Service(admin: true).CreateRootAsync(_admin, Root(1000.5m));

        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal(_branch.Id, result.DepartmentId);
        Assert.Null(result.EmployeeId);
        Assert.Null(result.ParentAssignmentId);
        Assert.Null(result.SplitDimension);
        Assert.Equal(1, result.Version);
        Assert.Equal(1000.5m, result.TargetValue);
        Assert.Equal(Now, result.AssignedAt);
        Assert.Equal(Now, result.ApprovedAt);
        Assert.Equal("Q4", result.ChangeReason);
        Assert.Single(_db.TargetAssignments);
    }

    [Fact]
    public async Task CreateRoot_ByANonAdmin_IsForbiddenAndStoresNothing()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).CreateRootAsync(_manager, Root()));

        Assert.Empty(_db.TargetAssignments);
    }

    [Fact]
    public async Task Allocate_CreatesEveryChildAsAnActiveOrgSplitInheritingTheParent()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dto = new AllocateTargetDto
        {
            Allocations =
            [
                new() { DepartmentId = _deptOther.Id, TargetValue = 400 },
                new() { EmployeeId = _member.Id, TargetValue = 200 },
            ],
            ChangeReason = "split",
        };

        var children = await Service(admin: true).AllocateAsync(_admin, parent.Id, dto);

        Assert.Equal(2, children.Count);
        Assert.All(children, c =>
        {
            Assert.Equal(parent.Id, c.ParentAssignmentId);
            Assert.Equal("ORG", c.SplitDimension);
            Assert.Equal("ACTIVE", c.Status);
            Assert.Equal(_kpiId, c.KpiDefinitionId);
            Assert.Equal(_periodId, c.EvaluationPeriodId);
            Assert.Equal("split", c.ChangeReason);
        });
        Assert.Equal(_deptOther.Id, children[0].DepartmentId);
        Assert.Equal(_member.Id, children[1].EmployeeId);
        Assert.Equal(3, _db.TargetAssignments.Count());
    }

    [Fact]
    public async Task Allocate_ByAManagerOfAnotherDepartment_IsForbidden()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_deptOther);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 1 }] };

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).AllocateAsync(_manager, parent.Id, dto));

        Assert.Single(_db.TargetAssignments);
    }

    [Fact]
    public async Task Allocate_ByTheManagerOfTheOwningDepartment_Succeeds()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 600 }] };

        var children = await Service(admin: false).AllocateAsync(_manager, parent.Id, dto);

        Assert.Single(children);
    }

    [Fact]
    public async Task Allocate_OfAnUnknownParent_IsNotFound()
    {
        await SeedAsync();
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 1 }] };

        await Assert.ThrowsAsync<NotFoundException>(() => Service(admin: true).AllocateAsync(_admin, Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task Get_AllowsAdminsAndTheOwningDepartment_AndForbidsOthers()
    {
        await SeedAsync();
        var own = await StoreDepartmentTargetAsync(_dept);
        var other = await StoreDepartmentTargetAsync(_deptOther);

        Assert.Equal(own.Id, (await Service(admin: false).GetAsync(_manager, own.Id)).Id);
        Assert.Equal(other.Id, (await Service(admin: true).GetAsync(_admin, other.Id)).Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).GetAsync(_manager, other.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(admin: true).GetAsync(_admin, Guid.NewGuid()));
    }

    [Fact]
    public async Task Get_ATargetOwnedByAnEmployeeFollowsTheEmployeesDepartment()
    {
        await SeedAsync();
        var target = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = _kpiId,
            EvaluationPeriodId = _periodId,
            EmployeeId = _member.Id,
            Version = 1,
            TargetValue = 5,
            Status = TargetAssignmentStatus.Active,
            ApprovedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        _db.TargetAssignments.Add(target);
        await _db.SaveChangesAsync();

        Assert.Equal(_member.Id, (await Service(admin: false).GetAsync(_manager, target.Id)).EmployeeId);
    }

    private EvaluationPeriod AddPeriod(string code, PeriodType type, Guid? parent, Guid? id = null)
    {
        var period = new EvaluationPeriod
        {
            Id = id ?? Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = type,
            ParentPeriodId = parent,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(period);
        return period;
    }

    /// <summary>Year (= _periodId) > H1 > Q1; plus an unrelated year.</summary>
    private async Task<(Guid h1, Guid q1, Guid otherYear)> SeedPeriodsAsync()
    {
        AddPeriod("2026", PeriodType.Year, null, _periodId);
        var h1 = AddPeriod("H1", PeriodType.HalfYear, _periodId);
        var q1 = AddPeriod("Q1", PeriodType.Quarter, h1.Id);
        var other = AddPeriod("2027", PeriodType.Year, null);
        await _db.SaveChangesAsync();
        return (h1.Id, q1.Id, other.Id);
    }

    [Fact]
    public async Task AllocateByPeriod_CreatesPeriodChildrenKeepingTheOwner()
    {
        await SeedAsync();
        var (h1, q1, _) = await SeedPeriodsAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dto = new AllocateTargetByPeriodDto
        {
            Allocations = [new() { EvaluationPeriodId = h1, TargetValue = 400 }, new() { EvaluationPeriodId = q1, TargetValue = 200 }],
        };

        var children = await Service(admin: false).AllocateByPeriodAsync(_manager, parent.Id, dto);

        Assert.Equal(2, children.Count);
        Assert.All(children, c =>
        {
            Assert.Equal("PERIOD", c.SplitDimension);
            Assert.Equal(_dept.Id, c.DepartmentId);
            Assert.Null(c.EmployeeId);
            Assert.Equal(parent.Id, c.ParentAssignmentId);
        });
        Assert.Equal(q1, children[1].EvaluationPeriodId);
    }

    [Fact]
    public async Task AllocateByPeriod_RejectsDuplicateSameOrNonDescendantPeriods()
    {
        await SeedAsync();
        var (h1, _, otherYear) = await SeedPeriodsAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var service = Service(admin: true);

        AllocateTargetByPeriodDto Dto(params Guid[] ids) => new()
        {
            Allocations = [.. ids.Select(id => new TargetPeriodAllocationItemDto { EvaluationPeriodId = id, TargetValue = 1 })],
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.AllocateByPeriodAsync(_admin, parent.Id, Dto(h1, h1)));
        await Assert.ThrowsAsync<ValidationException>(() => service.AllocateByPeriodAsync(_admin, parent.Id, Dto(_periodId)));
        await Assert.ThrowsAsync<ValidationException>(() => service.AllocateByPeriodAsync(_admin, parent.Id, Dto(otherYear)));
        Assert.Single(_db.TargetAssignments);
    }

    [Fact]
    public async Task AllocateByPeriod_ByAManagerOfAnotherDepartment_IsForbidden()
    {
        await SeedAsync();
        var (h1, _, _) = await SeedPeriodsAsync();
        var parent = await StoreDepartmentTargetAsync(_deptOther);
        var dto = new AllocateTargetByPeriodDto { Allocations = [new() { EvaluationPeriodId = h1, TargetValue = 1 }] };

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).AllocateByPeriodAsync(_manager, parent.Id, dto));
    }

    [Fact]
    public async Task List_ScopesNonAdminsToTheirDepartmentTree()
    {
        await SeedAsync();
        var own = await StoreDepartmentTargetAsync(_dept);
        var other = await StoreDepartmentTargetAsync(_deptOther);
        var branch = await StoreDepartmentTargetAsync(_branch);

        var mine = await Service(admin: false).ListAsync(_manager, new TargetAssignmentQueryDto());
        var all = await Service(admin: true).ListAsync(_admin, new TargetAssignmentQueryDto());
        var filtered = await Service(admin: true).ListAsync(_admin, new TargetAssignmentQueryDto { DepartmentId = _deptOther.Id });

        Assert.Equal([own.Id], mine.Select(t => t.Id));
        Assert.Equal(3, all.Count);
        Assert.Equal([other.Id], filtered.Select(t => t.Id));
        Assert.DoesNotContain(branch.Id, mine.Select(t => t.Id));
    }

    [Fact]
    public async Task List_RejectsOutOfScopeUnknownDepartmentsAndBadStatus()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(admin: false).ListAsync(_manager, new TargetAssignmentQueryDto { DepartmentId = _deptOther.Id }));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(admin: true).ListAsync(_admin, new TargetAssignmentQueryDto { DepartmentId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service(admin: true).ListAsync(_admin, new TargetAssignmentQueryDto { Status = "NOPE" }));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakePermissions(bool admin) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(admin && resource == "target" && action == "manage" && requiredScope == "All");

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
