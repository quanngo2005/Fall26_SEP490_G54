using G54.BLL.Common;
using G54.BLL.Common.Constants;
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

    private TargetAssignmentService Service(bool admin, RecordingNotifier? notifier = null, Guid? managerAccountId = null) => new(
        new TargetAssignmentRepository(_db),
        new TargetAssignmentFeedbackRepository(_db),
        new Repository<EvaluationPeriod>(_db),
        new FakePermissions(admin, managerAccountId),
        new WeekdayWorkingDayCalendar(),
        notifier ?? new RecordingNotifier(),
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

    /// <summary>Stores the Open period of <see cref="_periodId"/> (the repository joins it when listing children).</summary>
    private async Task StorePeriodAsync(PeriodStatus status = PeriodStatus.Open, DateOnly? hardLockedThrough = null)
    {
        _db.EvaluationPeriods.Add(new EvaluationPeriod
        {
            Id = _periodId,
            Code = "Q4",
            Name = "Q4",
            PeriodType = PeriodType.Quarter,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = status,
            HardLockedThrough = hardLockedThrough,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();
    }

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
    public async Task Reallocate_SupersedesEveryChangedChildAndCreatesTheNextVersion()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var created = await Service(admin: false).AllocateAsync(
            _manager,
            parent.Id,
            new AllocateTargetDto
            {
                Allocations =
                [
                    new() { DepartmentId = _deptOther.Id, TargetValue = 400 },
                    new() { EmployeeId = _member.Id, TargetValue = 200 },
                ],
            });

        var changed = await Service(admin: false).ReallocateAsync(
            _manager,
            parent.Id,
            new ReallocateTargetDto
            {
                Allocations =
                [
                    new() { DepartmentId = _deptOther.Id, TargetValue = 400 },
                    new() { EmployeeId = _member.Id, TargetValue = 200.5m },
                ],
                ChangeReason = "Góp ý của nhân viên",
            });

        var next = Assert.Single(changed);
        Assert.Equal(_member.Id, next.EmployeeId);
        Assert.Equal(2, next.Version);
        Assert.Equal(200.5m, next.TargetValue);
        Assert.Equal("ACTIVE", next.Status);
        Assert.Equal("NOT_VIEWED", next.FeedbackStatus);
        Assert.Equal(parent.Id, next.ParentAssignmentId);
        Assert.Equal("Góp ý của nhân viên", next.ChangeReason);
        Assert.Equal(TargetAssignmentStatus.Superseded, _db.TargetAssignments.Single(t => t.Id == created[1].Id).Status);
        Assert.Equal(TargetAssignmentStatus.Active, _db.TargetAssignments.Single(t => t.Id == created[0].Id).Status);
        Assert.Equal(4, _db.TargetAssignments.Count());
    }

    [Fact]
    public async Task Reallocate_LeavesAnUnchangedDepartmentAlone()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        await Service(admin: true).AllocateAsync(
            _admin,
            parent.Id,
            new AllocateTargetDto { Allocations = [new() { DepartmentId = _deptOther.Id, TargetValue = 600 }] });

        var changed = await Service(admin: true).ReallocateAsync(
            _admin,
            parent.Id,
            new ReallocateTargetDto { Allocations = [new() { DepartmentId = _deptOther.Id, TargetValue = 600 }], ChangeReason = "x" });

        Assert.Empty(changed);
        Assert.Equal(2, _db.TargetAssignments.Count());
    }

    [Fact]
    public async Task Reallocate_KeepsAnUnchangedEmployeeAsItIs()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var created = await Service(admin: true).AllocateAsync(
            _admin,
            parent.Id,
            new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 600 }] });

        var changed = await Service(admin: true).ReallocateAsync(
            _admin,
            parent.Id,
            new ReallocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 600 }], ChangeReason = "x" });

        Assert.Empty(changed);
        Assert.Equal(TargetAssignmentStatus.Active, _db.TargetAssignments.Single(t => t.Id == created[0].Id).Status);
    }

    [Fact]
    public async Task Reallocate_WithOtherOwnersThanTheAllocatedOnes_IsRejected()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        await Service(admin: true).AllocateAsync(
            _admin,
            parent.Id,
            new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 600 }] });
        var dto = new ReallocateTargetDto { Allocations = [new() { DepartmentId = _deptOther.Id, TargetValue = 600 }], ChangeReason = "x" };

        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).ReallocateAsync(_admin, parent.Id, dto));

        Assert.Equal(2, _db.TargetAssignments.Count());
    }

    [Fact]
    public async Task Reallocate_MayAddAnOwnerThatWasNotAllocatedYet()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        await Service(admin: true).AllocateAsync(
            _admin,
            parent.Id,
            new AllocateTargetDto { Allocations = [new() { DepartmentId = _deptOther.Id, TargetValue = 600 }] });

        var changed = await Service(admin: true).ReallocateAsync(
            _admin,
            parent.Id,
            new ReallocateTargetDto
            {
                Allocations =
                [
                    new() { DepartmentId = _deptOther.Id, TargetValue = 400 },
                    new() { EmployeeId = _member.Id, TargetValue = 200 },
                ],
                ChangeReason = "thêm nhân viên",
            });

        Assert.Equal(2, changed.Count);
        Assert.Equal(2, changed.Single(t => t.DepartmentId == _deptOther.Id).Version);
        var added = changed.Single(t => t.EmployeeId == _member.Id);
        Assert.Equal(1, added.Version);
        Assert.Equal(parent.Id, added.ParentAssignmentId);
        Assert.Equal("ACTIVE", added.Status);
    }

    [Fact]
    public async Task Reallocate_ByAManagerOfAnotherDepartment_IsForbidden()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_deptOther);
        var dto = new ReallocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 1 }], ChangeReason = "x" };

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).ReallocateAsync(_manager, parent.Id, dto));
    }

    [Fact]
    public async Task Allocate_ByTheManagerOfAParentDepartment_ToTheEmployeesOfATeamBelow_Succeeds()
    {
        await SeedAsync();
        var team = Department("T", DepartmentLevelType.Team, _dept.Id);
        var teammate = Employee("TM", team.Id);
        _db.Departments.Add(team);
        _db.Employees.Add(teammate);
        await _db.SaveChangesAsync();
        var teamTarget = await StoreDepartmentTargetAsync(team);

        var children = await Service(admin: false).AllocateAsync(
            _manager,
            teamTarget.Id,
            new AllocateTargetDto { Allocations = [new() { EmployeeId = teammate.Id, TargetValue = 600 }] });

        Assert.Single(children);
    }

    [Fact]
    public async Task Reallocate_OfAChildWithItsOwnChildren_SupersedesThoseToo()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var team = Department("T", DepartmentLevelType.Team, _dept.Id);
        var teammate = Employee("TM", team.Id);
        _db.Departments.Add(team);
        _db.Employees.Add(teammate);
        await _db.SaveChangesAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var split = await Service(admin: true).AllocateAsync(
            _admin,
            parent.Id,
            new AllocateTargetDto { Allocations = [new() { DepartmentId = team.Id, TargetValue = 600 }] });
        var members = await Service(admin: true).AllocateAsync(
            _admin,
            split[0].Id,
            new AllocateTargetDto { Allocations = [new() { EmployeeId = teammate.Id, TargetValue = 600 }] });

        await Service(admin: true).ReallocateAsync(
            _admin,
            parent.Id,
            new ReallocateTargetDto { Allocations = [new() { DepartmentId = team.Id, TargetValue = 650 }], ChangeReason = "x" });

        Assert.Equal(TargetAssignmentStatus.Superseded, _db.TargetAssignments.Single(t => t.Id == members[0].Id).Status);
    }

    [Fact]
    public async Task Allocate_ToAnEmployeeWhoseEarlierShareWasSuperseded_UsesTheNextVersion()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var team = Department("T", DepartmentLevelType.Team, _dept.Id);
        var teammate = Employee("TM", team.Id);
        _db.Departments.Add(team);
        _db.Employees.Add(teammate);
        await _db.SaveChangesAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var split = await Service(admin: true).AllocateAsync(
            _admin,
            parent.Id,
            new AllocateTargetDto { Allocations = [new() { DepartmentId = team.Id, TargetValue = 600 }] });
        await Service(admin: true).AllocateAsync(
            _admin,
            split[0].Id,
            new AllocateTargetDto { Allocations = [new() { EmployeeId = teammate.Id, TargetValue = 600 }] });
        var resized = await Service(admin: true).ReallocateAsync(
            _admin,
            parent.Id,
            new ReallocateTargetDto { Allocations = [new() { DepartmentId = team.Id, TargetValue = 650 }], ChangeReason = "x" });

        var again = await Service(admin: true).AllocateAsync(
            _admin,
            resized[0].Id,
            new AllocateTargetDto { Allocations = [new() { EmployeeId = teammate.Id, TargetValue = 650 }] });

        Assert.Equal(2, again[0].Version);
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

    // ----- a locked period rejects every write ----------------------------------------------------

    private async Task LockPeriodAsync(Guid periodId, PeriodStatus status)
    {
        var period = await _db.EvaluationPeriods.SingleAsync(p => p.Id == periodId);
        period.Status = status;
        await _db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(PeriodStatus.Locked)]
    [InlineData(PeriodStatus.Closed)]
    public async Task CreateRoot_InALockedPeriod_ThrowsPeriodLockedAndStoresNothing(PeriodStatus status)
    {
        await SeedAsync();
        await SeedPeriodsAsync();
        await LockPeriodAsync(_periodId, status);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service(admin: true).CreateRootAsync(_admin, Root()));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Empty(_db.TargetAssignments);
    }

    [Theory]
    [InlineData(PeriodStatus.Open)]
    [InlineData(PeriodStatus.InReview)]
    public async Task CreateRoot_InAnOpenOrInReviewPeriod_StillSucceeds(PeriodStatus status)
    {
        await SeedAsync();
        await SeedPeriodsAsync();
        await LockPeriodAsync(_periodId, status);

        var result = await Service(admin: true).CreateRootAsync(_admin, Root());

        Assert.Equal("ACTIVE", result.Status);
    }

    [Fact]
    public async Task Allocate_WhenTheParentPeriodIsLocked_ThrowsPeriodLockedAndStoresNothing()
    {
        await SeedAsync();
        await SeedPeriodsAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        await LockPeriodAsync(_periodId, PeriodStatus.Locked);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 600 }] };

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, dto));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Single(_db.TargetAssignments);
    }

    [Fact]
    public async Task AllocateByPeriod_WhenAnyTargetPeriodIsLocked_ThrowsPeriodLockedAndStoresNothing()
    {
        await SeedAsync();
        var (h1, q1, _) = await SeedPeriodsAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        await LockPeriodAsync(q1, PeriodStatus.Closed);
        var dto = new AllocateTargetByPeriodDto
        {
            Allocations = [new() { EvaluationPeriodId = h1, TargetValue = 400 }, new() { EvaluationPeriodId = q1, TargetValue = 200 }],
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service(admin: true).AllocateByPeriodAsync(_admin, parent.Id, dto));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Single(_db.TargetAssignments);
    }

    [Fact]
    public async Task AllocateByPeriod_WhenTheParentPeriodIsLocked_ThrowsPeriodLocked()
    {
        await SeedAsync();
        var (h1, _, _) = await SeedPeriodsAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        await LockPeriodAsync(_periodId, PeriodStatus.Locked);
        var dto = new AllocateTargetByPeriodDto { Allocations = [new() { EvaluationPeriodId = h1, TargetValue = 1 }] };

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service(admin: true).AllocateByPeriodAsync(_admin, parent.Id, dto));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
    }

    [Fact]
    public async Task LockedPeriod_DoesNotHideForbiddenOrNotFound()
    {
        await SeedAsync();
        await SeedPeriodsAsync();
        var other = await StoreDepartmentTargetAsync(_deptOther);
        await LockPeriodAsync(_periodId, PeriodStatus.Locked);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 1 }] };

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).AllocateAsync(_manager, other.Id, dto));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(admin: true).AllocateAsync(_admin, Guid.NewGuid(), dto));
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

    [Fact]
    public async Task Allocate_RejectsARowWithBothOrNeitherOwner()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var both = new AllocateTargetDto { Allocations = [new() { DepartmentId = _deptOther.Id, EmployeeId = _member.Id, TargetValue = 1 }] };
        var neither = new AllocateTargetDto { Allocations = [new() { TargetValue = 1 }] };

        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, both));
        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, neither));
    }

    [Fact]
    public async Task Allocate_RejectsDuplicateDepartmentOrEmployeeRows()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dupDept = new AllocateTargetDto
        {
            Allocations = [new() { DepartmentId = _deptOther.Id, TargetValue = 1 }, new() { DepartmentId = _deptOther.Id, TargetValue = 1 }],
        };
        var dupEmployee = new AllocateTargetDto
        {
            Allocations = [new() { EmployeeId = _member.Id, TargetValue = 1 }, new() { EmployeeId = _member.Id, TargetValue = 1 }],
        };

        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, dupDept));
        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, dupEmployee));
    }

    [Fact]
    public async Task Allocate_ToAnUnknownEmployee_IsNotFound()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = Guid.NewGuid(), TargetValue = 1 }] };

        await Assert.ThrowsAsync<NotFoundException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, dto));
    }

    [Fact]
    public async Task Allocate_ToAnInactiveEmployee_IsRejected()
    {
        await SeedAsync();
        _member.Status = EmployeeStatus.Inactive;
        await _db.SaveChangesAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 1 }] };

        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, dto));
    }

    [Fact]
    public async Task Allocate_ToAnEmployeeOutsideTheParentDepartmentTree_IsRejected()
    {
        await SeedAsync();
        var outsider = Employee("OUT", _deptOther.Id);
        _db.Employees.Add(outsider);
        await _db.SaveChangesAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = outsider.Id, TargetValue = 1 }] };

        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, dto));
    }

    [Fact]
    public async Task Allocate_ToAValidActiveEmployeeInTheDepartmentTree_Succeeds()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 600 }] };

        var children = await Service(admin: true).AllocateAsync(_admin, parent.Id, dto);

        Assert.Single(children);
        Assert.Equal(_member.Id, children[0].EmployeeId);
    }

    [Fact]
    public async Task Allocate_ToAnEmployeeWhoAlreadyHasAnActiveTarget_IsConflict()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        await StoreEmployeeTargetAsync(_member);
        var dto = new AllocateTargetDto { Allocations = [new() { EmployeeId = _member.Id, TargetValue = 600 }] };

        await Assert.ThrowsAsync<ConflictException>(() => Service(admin: true).AllocateAsync(_admin, parent.Id, dto));
    }

    [Fact]
    public async Task Allocate_ToADepartmentTarget_DoesNotRequireEmployeeChecks()
    {
        await SeedAsync();
        var parent = await StoreDepartmentTargetAsync(_dept);
        // parent.DepartmentId is set here, so an employee row would be allowed; this proves pure-department rows still pass.
        var dto = new AllocateTargetDto { Allocations = [new() { DepartmentId = _deptOther.Id, TargetValue = 1 }] };

        var children = await Service(admin: true).AllocateAsync(_admin, parent.Id, dto);

        Assert.Single(children);
    }

    [Fact]
    public async Task Confirm_ByANonOwner_IsForbidden()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).ConfirmAsync(_manager, target.Id));
    }

    [Fact]
    public async Task Confirm_ByTheOwner_Succeeds()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);
        var memberAccountId = AddAccount(_member).Id;
        await _db.SaveChangesAsync();

        var feedback = await Service(admin: false).ConfirmAsync(memberAccountId, target.Id);

        Assert.Equal("CONFIRM", feedback.Type);
    }

    [Fact]
    public async Task Confirm_TwiceWithoutAnUpdate_IsConflict()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);
        var memberAccountId = AddAccount(_member).Id;
        await _db.SaveChangesAsync();
        var service = Service(admin: false);
        await service.ConfirmAsync(memberAccountId, target.Id);

        await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(memberAccountId, target.Id));
    }

    [Fact]
    public async Task Confirm_AfterTheTargetWasUpdated_IsAllowedAgain()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);
        var memberAccountId = AddAccount(_member).Id;
        await _db.SaveChangesAsync();
        var service = Service(admin: false);
        await service.ConfirmAsync(memberAccountId, target.Id);

        target.UpdatedAt = Now.AddDays(1);
        await _db.SaveChangesAsync();

        var feedback = await service.ConfirmAsync(memberAccountId, target.Id);
        Assert.Equal("CONFIRM", feedback.Type);
    }

    [Fact]
    public async Task List_FeedbackAt_IsSetOnlyForAnsweredEmployeeTargets()
    {
        await SeedAsync();
        var answered = await StoreEmployeeTargetAsync(_member);
        var memberAccountId = AddAccount(_member).Id;
        await _db.SaveChangesAsync();
        var service = Service(admin: true);
        var untouched = (await service.ListAsync(_admin, new TargetAssignmentQueryDto())).Single(t => t.Id == answered.Id);
        await service.ConfirmAsync(memberAccountId, answered.Id);

        var listed = (await service.ListAsync(_admin, new TargetAssignmentQueryDto())).ToList();
        var confirmed = listed.Single(t => t.Id == answered.Id);

        Assert.Equal("NOT_VIEWED", untouched.FeedbackStatus);
        Assert.Null(untouched.FeedbackAt);
        Assert.Equal("CONFIRMED", confirmed.FeedbackStatus);
        Assert.NotNull(confirmed.FeedbackAt);
        Assert.All(listed.Where(t => t.EmployeeId is null), t => Assert.Null(t.FeedbackAt));
    }

    [Fact]
    public async Task Comment_LongerThan500Characters_IsRejected()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);
        var memberAccountId = AddAccount(_member).Id;
        await _db.SaveChangesAsync();
        var dto = new CreateTargetFeedbackCommentDto { Comment = new string('x', 501) };

        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: false).CommentAsync(memberAccountId, target.Id, dto));
    }

    [Fact]
    public async Task Comment_ByTheOwner_Succeeds()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);
        var memberAccountId = AddAccount(_member).Id;
        await _db.SaveChangesAsync();
        var dto = new CreateTargetFeedbackCommentDto { Comment = "Cần hỗ trợ thêm" };

        var feedback = await Service(admin: false).CommentAsync(memberAccountId, target.Id, dto);

        Assert.Equal("COMMENT", feedback.Type);
        Assert.Equal("Cần hỗ trợ thêm", feedback.Comment);
    }

    [Fact]
    public async Task Feedback_ReadByAPeerInTheSameDepartment_IsForbidden()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);
        var peer = Employee("PEER", _dept.Id);
        _db.Employees.Add(peer);
        var peerAccountId = AddAccount(peer).Id;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(admin: false).GetFeedbackAsync(peerAccountId, target.Id));
    }

    [Fact]
    public async Task Feedback_ReadByTheDepartmentManager_Succeeds()
    {
        await SeedAsync();
        var target = await StoreEmployeeTargetAsync(_member);
        var service = new TargetAssignmentService(
            new TargetAssignmentRepository(_db),
            new TargetAssignmentFeedbackRepository(_db),
            new Repository<EvaluationPeriod>(_db),
            new FakePermissions(admin: false, managerAccountId: _manager),
            new WeekdayWorkingDayCalendar(),
            new RecordingNotifier(),
            new FixedTime());

        var feedback = await service.GetFeedbackAsync(_manager, target.Id);

        Assert.Empty(feedback);
    }

    [Fact]
    public async Task Feedback_ReadByTheManagerOfAParentDepartment_Succeeds()
    {
        await SeedAsync();
        var team = Department("T", DepartmentLevelType.Team, _dept.Id);
        _db.Departments.Add(team);
        var teamMember = Employee("TM", team.Id);
        _db.Employees.Add(teamMember);
        await _db.SaveChangesAsync();
        var target = await StoreEmployeeTargetAsync(teamMember);
        var service = new TargetAssignmentService(
            new TargetAssignmentRepository(_db),
            new TargetAssignmentFeedbackRepository(_db),
            new Repository<EvaluationPeriod>(_db),
            new FakePermissions(admin: false, managerAccountId: _manager),
            new WeekdayWorkingDayCalendar(),
            new RecordingNotifier(),
            new FixedTime());

        var feedback = await service.GetFeedbackAsync(_manager, target.Id);

        Assert.Empty(feedback);
    }

    private async Task<TargetAssignment> StoreEmployeeTargetAsync(Employee owner)
    {
        var target = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = _kpiId,
            EvaluationPeriodId = _periodId,
            EmployeeId = owner.Id,
            Version = 1,
            TargetValue = 100,
            Status = TargetAssignmentStatus.Active,
            ApprovedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        _db.TargetAssignments.Add(target);
        await _db.SaveChangesAsync();
        return target;
    }

    // ----- ReviseAsync (BR-15 mid-period target change) --------------------------------------

    private static ReviseTargetAssignmentDto ReviseDto(
        decimal targetValue,
        DateOnly effectiveDate,
        string? reason = "Nghỉ phép",
        List<TargetAllocationItemDto>? childAllocations = null) => new()
        {
            TargetValue = targetValue,
            EffectiveDate = effectiveDate,
            Reason = reason,
            ChildAllocations = childAllocations,
        };

    [Fact]
    public async Task Revise_NoChildren_FreezesOldVersionAndActivatesTheNewOneFromTheEffectiveDate()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var current = await StoreDepartmentTargetAsync(_dept);
        current.EffectiveFrom = new DateOnly(2026, 10, 1);
        await _db.SaveChangesAsync();
        var effectiveDate = new DateOnly(2026, 11, 15);

        var result = await Service(admin: true).ReviseAsync(_admin, current.Id, ReviseDto(900, effectiveDate));

        await _db.Entry(current).ReloadAsync();
        Assert.Equal(TargetAssignmentStatus.Superseded, current.Status);
        Assert.Equal(effectiveDate.AddDays(-1), current.EffectiveTo);

        var created = Assert.Single(result);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal(current.Version + 1, created.Version);
        Assert.Equal(900, created.TargetValue);
        Assert.Equal(effectiveDate, created.EffectiveFrom);
        Assert.Null(created.EffectiveTo);
        Assert.Equal(current.Id, created.PreviousVersionId);
        Assert.Equal("Nghỉ phép", created.ChangeReason);
    }

    [Fact]
    public async Task Revise_WithActiveChildren_FreezesThemAndCreatesTheNextVersionOfEach()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_branch);
        var child1 = await StoreDepartmentTargetAsync(_dept);
        child1.ParentAssignmentId = parent.Id;
        child1.SplitDimension = TargetSplitDimension.Org;
        var child2 = await StoreDepartmentTargetAsync(_deptOther);
        child2.ParentAssignmentId = parent.Id;
        child2.SplitDimension = TargetSplitDimension.Org;
        await _db.SaveChangesAsync();
        var effectiveDate = new DateOnly(2026, 11, 15);

        var dto = ReviseDto(900, effectiveDate, childAllocations:
        [
            new() { DepartmentId = _dept.Id, TargetValue = 500 },
            new() { DepartmentId = _deptOther.Id, TargetValue = 400 },
        ]);

        var result = await Service(admin: true).ReviseAsync(_admin, parent.Id, dto);

        Assert.Equal(3, result.Count);
        await _db.Entry(child1).ReloadAsync();
        await _db.Entry(child2).ReloadAsync();
        Assert.Equal(TargetAssignmentStatus.Superseded, child1.Status);
        Assert.Equal(TargetAssignmentStatus.Superseded, child2.Status);
        Assert.Equal(effectiveDate.AddDays(-1), child1.EffectiveTo);

        var newChild1 = result.Single(r => r.DepartmentId == _dept.Id);
        Assert.Equal(child1.Id, newChild1.PreviousVersionId);
        Assert.Equal(500, newChild1.TargetValue);
        Assert.Equal("ACTIVE", newChild1.Status);
    }

    [Fact]
    public async Task Revise_WhenThePeriodIsLocked_ThrowsConflictAndStoresNothing()
    {
        await SeedAsync();
        await StorePeriodAsync(PeriodStatus.Locked);
        var current = await StoreDepartmentTargetAsync(_dept);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => Service(admin: true).ReviseAsync(_admin, current.Id, ReviseDto(900, new DateOnly(2026, 11, 15))));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Single(_db.TargetAssignments);
    }

    [Fact]
    public async Task Revise_WhenTheMonthIsHardLocked_ThrowsMonthHardLocked()
    {
        await SeedAsync();
        await StorePeriodAsync(hardLockedThrough: new DateOnly(2026, 11, 10));
        var current = await StoreDepartmentTargetAsync(_dept);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => Service(admin: true).ReviseAsync(_admin, current.Id, ReviseDto(900, new DateOnly(2026, 11, 15))));

        Assert.Equal(ErrorCodes.MonthHardLocked, ex.ErrorCode);
    }

    [Fact]
    public async Task Revise_EffectiveDateNotAfterTheCurrentEffectiveFrom_ThrowsValidation()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var current = await StoreDepartmentTargetAsync(_dept);
        current.EffectiveFrom = new DateOnly(2026, 11, 1);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(
            () => Service(admin: true).ReviseAsync(_admin, current.Id, ReviseDto(900, new DateOnly(2026, 11, 1))));
    }

    [Fact]
    public async Task Revise_WithoutAReason_ThrowsValidation()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var current = await StoreDepartmentTargetAsync(_dept);

        await Assert.ThrowsAsync<ValidationException>(
            () => Service(admin: true).ReviseAsync(_admin, current.Id, ReviseDto(900, new DateOnly(2026, 11, 15), reason: "")));
    }

    [Fact]
    public async Task Revise_WhenChildAllocationsDoNotSumToTheNewTargetValue_ThrowsValidation()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var parent = await StoreDepartmentTargetAsync(_branch);
        var child1 = await StoreDepartmentTargetAsync(_dept);
        child1.ParentAssignmentId = parent.Id;
        await _db.SaveChangesAsync();

        var dto = ReviseDto(900, new DateOnly(2026, 11, 15), childAllocations:
        [
            new() { DepartmentId = _dept.Id, TargetValue = 500 },
        ]);

        await Assert.ThrowsAsync<ValidationException>(() => Service(admin: true).ReviseAsync(_admin, parent.Id, dto));
    }

    [Fact]
    public async Task Revise_ByACallerOutsideTheOwningDepartment_IsForbidden()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var current = await StoreDepartmentTargetAsync(_deptOther);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(admin: false, managerAccountId: _manager).ReviseAsync(_manager, current.Id, ReviseDto(900, new DateOnly(2026, 11, 15))));
    }

    [Fact]
    public async Task Revise_WhenTheTargetIsNotActive_ThrowsInvalidState()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var current = await StoreDepartmentTargetAsync(_dept);
        current.Status = TargetAssignmentStatus.Superseded;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service(admin: true).ReviseAsync(_admin, current.Id, ReviseDto(900, new DateOnly(2026, 11, 15))));
    }

    [Fact]
    public async Task Revise_Success_PublishesANotificationForTheOwningEmployee()
    {
        await SeedAsync();
        await StorePeriodAsync();
        var current = await StoreEmployeeTargetAsync(_member);
        var notifier = new RecordingNotifier();

        await Service(admin: true, notifier).ReviseAsync(_admin, current.Id, ReviseDto(150, new DateOnly(2026, 11, 15)));

        var draft = Assert.Single(notifier.Drafts);
        Assert.Equal(_member.Id, draft.RecipientEmployeeId);
        Assert.Equal(NotificationType.TargetRevised, draft.Type);
        Assert.Equal(NotificationRelatedType.Target, draft.RelatedType);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Grants target:manage scope All to everyone when <paramref name="admin"/>, scope Dept to <paramref name="managerAccountId"/>.</summary>
    private sealed class FakePermissions(bool admin, Guid? managerAccountId = null) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(resource == "target" && action == "manage" &&
                ((admin && requiredScope == "All") || (id == managerAccountId && requiredScope == "Dept")));

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
