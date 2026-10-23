using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class TargetAssignmentServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly Dictionary<Guid, Guid> _accounts = [];

    private Department _branch = null!;
    private Department _dept = null!;
    private Department _deptOther = null!;
    private Department _team = null!;
    private Employee _adminEmployee = null!;
    private Employee _deptManager = null!;
    private Employee _teamMember = null!;
    private Employee _deptMember = null!;
    private KpiDefinition _sumKpi = null!;
    private KpiDefinition _ratioKpi = null!;
    private EvaluationPeriod _period = null!;

    public void Dispose() => _db.Dispose();

    /// <summary>BRANCH > (DEPT, DEPT_OTHER); DEPT > TEAM. Employees: admin (branch), manager + member (DEPT), teamMember (TEAM).</summary>
    private async Task SeedAsync()
    {
        _branch = Department("B", DepartmentLevelType.Branch, null);
        _dept = Department("D", DepartmentLevelType.Department, _branch.Id);
        _deptOther = Department("D2", DepartmentLevelType.Department, _branch.Id);
        _team = Department("T", DepartmentLevelType.Team, _dept.Id);
        _db.Departments.AddRange(_branch, _dept, _deptOther, _team);

        _adminEmployee = Employee("ADM", _branch.Id);
        _deptManager = Employee("MGR", _dept.Id);
        _deptMember = Employee("MEM", _dept.Id);
        _teamMember = Employee("TM", _team.Id);
        _db.Employees.AddRange(_adminEmployee, _deptManager, _deptMember, _teamMember);

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "G", UpdatedAt = Now };
        _db.KpiGroups.Add(group);
        _sumKpi = Kpi(group.Id, "SUM1", KpiAggregationType.Sum);
        _ratioKpi = Kpi(group.Id, "RATIO1", KpiAggregationType.Ratio);
        _db.KpiDefinitions.AddRange(_sumKpi, _ratioKpi);

        _period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-Q4",
            Name = "Q4",
            PeriodType = PeriodType.Quarter,
            Status = PeriodStatus.Open,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 12, 31),
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(_period);
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

    private static KpiDefinition Kpi(Guid groupId, string code, KpiAggregationType aggregation) => new()
    {
        Id = Guid.NewGuid(),
        KpiGroupId = groupId,
        Code = code,
        Name = code,
        Unit = "VND",
        AggregationType = aggregation,
        Direction = KpiDirection.HigherBetter,
        Status = KpiStatus.Active,
        UpdatedAt = Now,
    };

    /// <summary>One account per employee (the relationship is 1-1), reused on later calls.</summary>
    private async Task<Guid> AccountForAsync(Employee employee, bool locked = false)
    {
        if (_accounts.TryGetValue(employee.Id, out var existing))
        {
            return existing;
        }

        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = employee.EmployeeCode,
            PasswordHash = "h",
            IsLocked = locked,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        _accounts[employee.Id] = account.Id;
        return account.Id;
    }

    private TargetAssignmentService Service(string? scope) => new(_db, new FakePermissions(scope), new FixedTime());

    private CreateTargetAssignmentDto Root(Guid? branch = null, decimal? value = 1000, KpiDefinition? kpi = null) => new()
    {
        KpiDefinitionId = (kpi ?? _sumKpi).Id,
        EvaluationPeriodId = _period.Id,
        DepartmentId = branch ?? _branch.Id,
        TargetValue = value,
    };

    private static TargetAllocationItemDto ToDepartment(Department department, decimal value) =>
        new() { DepartmentId = department.Id, TargetValue = value };

    private static TargetAllocationItemDto ToEmployee(Employee employee, decimal value) =>
        new() { EmployeeId = employee.Id, TargetValue = value };

    private static AllocateTargetDto Split(params TargetAllocationItemDto[] items) => new() { Allocations = [.. items] };

    /// <summary>Creates the branch root as an administrator.</summary>
    private async Task<TargetAssignmentDto> CreateRootAsync(decimal value = 1000, KpiDefinition? kpi = null)
    {
        var admin = await AccountForAsync(_adminEmployee);
        return await Service("All").CreateRootAsync(admin, Root(value: value, kpi: kpi));
    }

    // ----- Root ----------------------------------------------------------------------------

    [Fact]
    public async Task CreateRoot_ForABranch_IsActiveAndApproved()
    {
        await SeedAsync();
        var admin = await AccountForAsync(_adminEmployee);

        var result = await Service("All").CreateRootAsync(admin, Root(value: 1000.5m));

        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal("DEPARTMENT", result.OwnerType);
        Assert.Equal("BRANCH", result.DepartmentLevelType);
        Assert.Equal(_branch.Id, result.DepartmentId);
        Assert.Null(result.ParentAssignmentId);
        Assert.Null(result.SplitDimension);
        Assert.Equal(1, result.Version);
        Assert.Equal(1000.5m, result.TargetValue);
        Assert.Equal(0m, result.AllocatedValue);
        Assert.Equal(1000.5m, result.RemainingValue);
        Assert.Equal(Now, result.AssignedAt);
        Assert.Equal(Now, result.ApprovedAt);
        Assert.Equal("SUM", result.KpiAggregationType);
        Assert.Equal("2026-Q4", result.PeriodCode);
    }

    [Fact]
    public async Task CreateRoot_ByANonAdmin_IsForbidden()
    {
        await SeedAsync();
        var manager = await AccountForAsync(_deptManager);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service("Dept").CreateRootAsync(manager, Root()));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service("Branch").CreateRootAsync(manager, Root()));
    }

    [Fact]
    public async Task CreateRoot_MustTargetABranch()
    {
        await SeedAsync();
        var admin = await AccountForAsync(_adminEmployee);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").CreateRootAsync(admin, Root(branch: _dept.Id)));

        Assert.Contains("chi nhánh", ex.Message);
    }

    [Fact]
    public async Task CreateRoot_RejectsMissingBranchAndBadValues()
    {
        await SeedAsync();
        var admin = await AccountForAsync(_adminEmployee);
        var noBranch = Root();
        noBranch.DepartmentId = null;
        var longReason = Root();
        longReason.ChangeReason = new string('x', 1001);

        await Assert.ThrowsAsync<ValidationException>(() => Service("All").CreateRootAsync(admin, noBranch));
        await Assert.ThrowsAsync<ValidationException>(() => Service("All").CreateRootAsync(admin, Root(value: null)));
        await Assert.ThrowsAsync<ValidationException>(() => Service("All").CreateRootAsync(admin, Root(value: -1)));
        await Assert.ThrowsAsync<ValidationException>(() => Service("All").CreateRootAsync(admin, Root(value: 0.00001m)));
        await Assert.ThrowsAsync<ValidationException>(() => Service("All").CreateRootAsync(admin, longReason));
    }

    [Fact]
    public async Task CreateRoot_ZeroIsAValidTarget()
    {
        await SeedAsync();
        var admin = await AccountForAsync(_adminEmployee);

        var result = await Service("All").CreateRootAsync(admin, Root(value: 0));

        Assert.Equal(0m, result.TargetValue);
    }

    [Fact]
    public async Task CreateRoot_UnknownReferencesAre404()
    {
        await SeedAsync();
        var admin = await AccountForAsync(_adminEmployee);
        var unknownKpi = Root();
        unknownKpi.KpiDefinitionId = Guid.NewGuid();
        var unknownPeriod = Root();
        unknownPeriod.EvaluationPeriodId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => Service("All").CreateRootAsync(admin, unknownKpi));
        await Assert.ThrowsAsync<NotFoundException>(() => Service("All").CreateRootAsync(admin, unknownPeriod));
        await Assert.ThrowsAsync<NotFoundException>(() => Service("All").CreateRootAsync(admin, Root(branch: Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateRoot_AnInactiveKpiIsRefused_AndAClosedPeriodIsAConflict()
    {
        await SeedAsync();
        var admin = await AccountForAsync(_adminEmployee);
        _sumKpi.Status = KpiStatus.Draft;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => Service("All").CreateRootAsync(admin, Root()));

        _sumKpi.Status = KpiStatus.Active;
        _period.Status = PeriodStatus.Locked;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => Service("All").CreateRootAsync(admin, Root()));
    }

    [Fact]
    public async Task CreateRoot_ASecondRootForTheSameBranchKpiAndPeriodIsAConflict()
    {
        await SeedAsync();
        await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);

        await Assert.ThrowsAsync<ConflictException>(() => Service("All").CreateRootAsync(admin, Root(value: 5)));
    }

    [Fact]
    public async Task CreateRoot_ALockedAccount_IsForbidden()
    {
        await SeedAsync();
        var locked = await AccountForAsync(_adminEmployee, locked: true);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service("All").CreateRootAsync(locked, Root()));
    }

    // ----- Allocate by level ----------------------------------------------------------------

    [Fact]
    public async Task Allocate_BranchSplitsToItsDepartments_AndTheParentReportsFullyAllocated()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000);
        var admin = await AccountForAsync(_adminEmployee);

        var result = await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 700), ToDepartment(_deptOther, 300)));

        Assert.Equal(2, result.Children.Count);
        Assert.All(result.Children, child =>
        {
            Assert.Equal(root.Id, child.ParentAssignmentId);
            Assert.Equal("ORG", child.SplitDimension);
            Assert.Equal("ACTIVE", child.Status);
            Assert.Equal(1, child.Version);
            Assert.Equal(Now, child.ApprovedAt);
        });
        Assert.Equal([700m, 300m], result.Children.Select(c => c.TargetValue));
        Assert.Equal(1000m, result.Parent.AllocatedValue);
        Assert.Equal(0m, result.Parent.RemainingValue);
    }

    [Fact]
    public async Task Allocate_DepartmentSplitsToATeamAndToItsOwnEmployees()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000);
        var admin = await AccountForAsync(_adminEmployee);
        var deptTarget = (await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 600), ToDepartment(_deptOther, 400)))).Children[0];

        var result = await Service("All").AllocateAsync(
            admin, deptTarget.Id, Split(ToDepartment(_team, 200), ToEmployee(_deptMember, 250), ToEmployee(_deptManager, 150)));

        Assert.Equal("TEAM", result.Children[0].DepartmentLevelType);
        Assert.Equal("EMPLOYEE", result.Children[1].OwnerType);
        Assert.Equal(_deptMember.Id, result.Children[1].EmployeeId);
        Assert.Null(result.Children[1].DepartmentId);
        Assert.Null(result.Children[1].AllocatedValue);
        Assert.Equal("MEM", result.Children[1].EmployeeCode);
    }

    [Fact]
    public async Task Allocate_TeamSplitsToAnEmployeeOfThatTeam()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000);
        var admin = await AccountForAsync(_adminEmployee);
        var deptTarget = (await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 600), ToDepartment(_deptOther, 400)))).Children[0];
        var teamTarget = (await Service("All").AllocateAsync(admin, deptTarget.Id, Split(ToDepartment(_team, 600)))).Children[0];

        var result = await Service("All").AllocateAsync(admin, teamTarget.Id, Split(ToEmployee(_teamMember, 600)));

        Assert.Equal(_teamMember.Id, result.Children.Single().EmployeeId);
    }

    [Fact]
    public async Task Allocate_ABranchCannotSkipToAnEmployeeOrATeam()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);

        var toEmployee = await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToEmployee(_deptMember, 1000))));
        var toTeam = await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_team, 1000))));

        Assert.Contains("phòng ban trước", toEmployee.Message);
        Assert.Contains("nhảy cấp", toTeam.Message);
    }

    [Fact]
    public async Task Allocate_ADepartmentCannotSplitToAnEmployeeOfAnotherDepartment()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);
        var deptTarget = (await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 600), ToDepartment(_deptOther, 400)))).Children[0];

        await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, deptTarget.Id, Split(ToEmployee(_teamMember, 600))));
    }

    [Fact]
    public async Task Allocate_ATargetOwnedByAnEmployeeCannotBeSplit()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);
        var deptTarget = (await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 600), ToDepartment(_deptOther, 400)))).Children[0];
        var employeeTarget = (await Service("All").AllocateAsync(admin, deptTarget.Id, Split(ToEmployee(_deptMember, 600)))).Children[0];

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, employeeTarget.Id, Split(ToEmployee(_deptManager, 600))));

        Assert.Contains("nhân viên", ex.Message);
    }

    [Fact]
    public async Task Allocate_ATargetCanOnlyBeSplitOnce()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);
        await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 700), ToDepartment(_deptOther, 300)));

        await Assert.ThrowsAsync<ConflictException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 1000))));
    }

    // ----- Sum rule -------------------------------------------------------------------------

    [Theory]
    [InlineData(600, 300)]
    [InlineData(700, 300.0001)]
    [InlineData(1000, 1)]
    public async Task Allocate_SumKpi_ChildrenMustTotalTheParentExactly(double first, double second)
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000);
        var admin = await AccountForAsync(_adminEmployee);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, (decimal)first), ToDepartment(_deptOther, (decimal)second))));

        Assert.Equal(ErrorCodes.TargetAllocationSumMismatch, ex.ErrorCode);
        Assert.Equal(422, ex.StatusCode);
        Assert.Empty(await _db.TargetAssignments.Where(t => t.ParentAssignmentId == root.Id).ToListAsync());
    }

    [Fact]
    public async Task Allocate_RatioKpi_EveryChildMayCarryTheSameLevel()
    {
        await SeedAsync();
        var root = await CreateRootAsync(3, _ratioKpi);
        var admin = await AccountForAsync(_adminEmployee);

        var result = await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 3), ToDepartment(_deptOther, 2.5m)));

        Assert.Equal([3m, 2.5m], result.Children.Select(c => c.TargetValue));
        Assert.Null(result.Parent.AllocatedValue);
        Assert.Null(result.Parent.RemainingValue);
    }

    // ----- Access ---------------------------------------------------------------------------

    [Fact]
    public async Task Allocate_ADepartmentManagerSplitsOwnTargetButNotAnotherDepartments()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000);
        var admin = await AccountForAsync(_adminEmployee);
        var targets = (await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 600), ToDepartment(_deptOther, 400)))).Children;
        var manager = await AccountForAsync(_deptManager);

        var own = await Service("Dept").AllocateAsync(manager, targets[0].Id, Split(ToEmployee(_deptMember, 350), ToEmployee(_deptManager, 250)));
        Assert.Equal(2, own.Children.Count);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service("Dept").AllocateAsync(manager, targets[1].Id, Split(ToEmployee(_deptManager, 400))));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service("Dept").AllocateAsync(manager, root.Id, Split(ToDepartment(_dept, 1000))));
    }

    [Fact]
    public async Task Allocate_ABranchScopeManagerSeesTheWholeBranch()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000);
        var branchManager = await AccountForAsync(_deptManager);

        var result = await Service("Branch").AllocateAsync(branchManager, root.Id, Split(ToDepartment(_dept, 100), ToDepartment(_deptOther, 900)));

        Assert.Equal(2, result.Children.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Own")]
    public async Task Allocate_WithoutAtLeastDepartmentScope_IsForbidden(string? scope)
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var account = await AccountForAsync(_deptManager);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(scope).AllocateAsync(account, root.Id, Split(ToDepartment(_dept, 1000))));
    }

    // ----- Allocation validation ------------------------------------------------------------

    [Fact]
    public async Task Allocate_RequiresAtLeastOneRowAndExactlyOneOwnerPerRow()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);

        await Assert.ThrowsAsync<ValidationException>(() => Service("All").AllocateAsync(admin, root.Id, new AllocateTargetDto()));
        await Assert.ThrowsAsync<ValidationException>(() => Service("All").AllocateAsync(admin, root.Id, Split()));
        await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(new TargetAllocationItemDto { TargetValue = 1000 })));
        await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(new TargetAllocationItemDto { DepartmentId = _dept.Id, EmployeeId = _deptMember.Id, TargetValue = 1000 })));
    }

    [Fact]
    public async Task Allocate_RejectsNegativeValuesDuplicatesAndTooManyRows()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000, _ratioKpi);
        var admin = await AccountForAsync(_adminEmployee);

        await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, -1))));
        await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 1), ToDepartment(_dept, 1))));
        var many = Enumerable.Range(0, 201).Select(_ => ToDepartment(_dept, 1)).ToArray();
        await Assert.ThrowsAsync<ValidationException>(() => Service("All").AllocateAsync(admin, root.Id, Split(many)));
    }

    [Fact]
    public async Task Allocate_UnknownOrInactiveOwnersAndParentsAreRefused()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);

        await Assert.ThrowsAsync<NotFoundException>(
            () => Service("All").AllocateAsync(admin, Guid.NewGuid(), Split(ToDepartment(_dept, 1000))));
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(new TargetAllocationItemDto { DepartmentId = Guid.NewGuid(), TargetValue = 1000 })));

        _deptOther.Status = RecordStatus.Inactive;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 500), ToDepartment(_deptOther, 500))));
    }

    [Fact]
    public async Task Allocate_ATerminatedOrUnknownEmployeeIsRefused()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);
        var deptTarget = (await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 600), ToDepartment(_deptOther, 400)))).Children[0];
        _deptMember.Status = EmployeeStatus.Terminated;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(
            () => Service("All").AllocateAsync(admin, deptTarget.Id, Split(ToEmployee(_deptMember, 600))));
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service("All").AllocateAsync(admin, deptTarget.Id, Split(new TargetAllocationItemDto { EmployeeId = Guid.NewGuid(), TargetValue = 600 })));
    }

    [Fact]
    public async Task Allocate_AParentThatIsNoLongerActiveOrAClosedPeriodIsAConflict()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var admin = await AccountForAsync(_adminEmployee);
        var stored = await _db.TargetAssignments.SingleAsync(t => t.Id == root.Id);
        stored.Status = TargetAssignmentStatus.Closed;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 1000))));

        stored.Status = TargetAssignmentStatus.Active;
        _period.Status = PeriodStatus.Closed;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 1000))));
    }

    [Fact]
    public async Task Allocate_ADepartmentThatAlreadyHasATargetForTheKpiAndPeriodIsAConflict()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000, _ratioKpi);
        var admin = await AccountForAsync(_adminEmployee);
        _db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = _ratioKpi.Id,
            EvaluationPeriodId = _period.Id,
            DepartmentId = _dept.Id,
            Version = 1,
            TargetValue = 1,
            Status = TargetAssignmentStatus.Draft,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 1))));
    }

    [Fact]
    public async Task Allocate_ChildrenInheritTheBusinessObjectiveAndKeepKpiAndPeriod()
    {
        await SeedAsync();
        var root = await CreateRootAsync();
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ",
            Name = "OBJ",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = Now,
        };
        _db.BusinessObjectives.Add(objective);
        (await _db.TargetAssignments.SingleAsync(t => t.Id == root.Id)).BusinessObjectiveId = objective.Id;
        await _db.SaveChangesAsync();
        var admin = await AccountForAsync(_adminEmployee);

        var result = await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 1000)));

        var child = result.Children.Single();
        Assert.Equal(_sumKpi.Id, child.KpiDefinitionId);
        Assert.Equal(_period.Id, child.EvaluationPeriodId);
        Assert.Equal(objective.Id, (await _db.TargetAssignments.SingleAsync(t => t.Id == child.Id)).BusinessObjectiveId);
    }

    // ----- Get ------------------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsTheTargetForAVisibleDepartment_AndForbidsOthers()
    {
        await SeedAsync();
        var root = await CreateRootAsync(1000);
        var admin = await AccountForAsync(_adminEmployee);
        var targets = (await Service("All").AllocateAsync(admin, root.Id, Split(ToDepartment(_dept, 600), ToDepartment(_deptOther, 400)))).Children;
        var employeeTarget = (await Service("All").AllocateAsync(admin, targets[0].Id, Split(ToEmployee(_deptMember, 600)))).Children[0];
        var manager = await AccountForAsync(_deptManager);

        Assert.Equal(targets[0].Id, (await Service("Dept").GetAsync(manager, targets[0].Id)).Id);
        Assert.Equal(employeeTarget.Id, (await Service("Dept").GetAsync(manager, employeeTarget.Id)).Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service("Dept").GetAsync(manager, targets[1].Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service("Dept").GetAsync(manager, root.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Service("Dept").GetAsync(manager, Guid.NewGuid()));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakePermissions(string? scope) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = scope is null
                ? []
                : [new UserPermissionDto("target", "manage", scope, "TARGET_MANAGE")];
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
