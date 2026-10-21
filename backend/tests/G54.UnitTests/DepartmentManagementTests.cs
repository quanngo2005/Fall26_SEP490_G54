using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Department;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class DepartmentManagementTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly Guid _adminAccount = Guid.NewGuid();

    public void Dispose() => _db.Dispose();

    private DepartmentService Service() => new(_db, new AllPermissions(), new FixedTime());

    private async Task<Department> SeedAsync(
        string code,
        DepartmentLevelType level,
        Guid? parentId = null,
        RecordStatus status = RecordStatus.Active)
    {
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            LevelType = level,
            ParentDepartmentId = parentId,
            Status = status,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Departments.Add(department);
        await _db.SaveChangesAsync();
        return department;
    }

    private async Task<Employee> SeedEmployeeAsync(Guid departmentId, EmployeeStatus status = EmployeeStatus.Active, string code = "E1")
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@x.local",
            DepartmentId = departmentId,
            JobPositionId = Guid.NewGuid(),
            Status = status,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return employee;
    }

    private static CreateDepartmentDto Create(string code, string level, Guid? parent = null) =>
        new() { Code = code, Name = $"Tên {code}", LevelType = level, ParentDepartmentId = parent };

    private static UpdateDepartmentDto Update(Department source, Action<UpdateDepartmentDto>? change = null)
    {
        var dto = new UpdateDepartmentDto
        {
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            LevelType = source.LevelType.ToString().ToUpperInvariant(),
            ParentDepartmentId = source.ParentDepartmentId,
        };
        change?.Invoke(dto);
        return dto;
    }

    // ----- Create ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_BuildsTheDepartmentUnderItsParent_AndNormalisesTheCode()
    {
        var branch = await SeedAsync("CN-HN", DepartmentLevelType.Branch);

        var result = await Service().CreateDepartmentAsync(new CreateDepartmentDto
        {
            Code = "  cn-hn-kd ",
            Name = "  Phòng Kinh doanh ",
            Description = "  Khối bán hàng ",
            LevelType = "department",
            ParentDepartmentId = branch.Id,
        });

        Assert.Equal("CN-HN-KD", result.Code);
        Assert.Equal("Phòng Kinh doanh", result.Name);
        Assert.Equal("Khối bán hàng", result.Description);
        Assert.Equal("DEPARTMENT", result.LevelType);
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal(branch.Id, result.ParentDepartmentId);
        Assert.Equal(["CN-HN"], result.Ancestors.Select(a => a.Code));
        Assert.Equal(Now, result.UpdatedAt);
    }

    [Fact]
    public async Task Create_ABranchWithoutAParent_Works()
    {
        var result = await Service().CreateDepartmentAsync(Create("CN-DN", "BRANCH"));

        Assert.Null(result.ParentDepartmentId);
        Assert.Empty(result.Ancestors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading")]
    [InlineData("has space")]
    [InlineData("tiếng-việt")]
    [InlineData("a/b")]
    public async Task Create_RejectsAnUnsafeCode(string code)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateDepartmentAsync(Create(code, "BRANCH")));
    }

    [Fact]
    public async Task Create_RejectsTooLongValuesBlankNamesAndUnknownLevels()
    {
        var service = Service();

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create(new string('A', 51), "BRANCH")));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(
            new CreateDepartmentDto { Code = "X", Name = "  ", LevelType = "BRANCH" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(
            new CreateDepartmentDto { Code = "X", Name = new string('n', 201), LevelType = "BRANCH" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(
            new CreateDepartmentDto { Code = "X", Name = "n", LevelType = "BRANCH", Description = new string('d', 1001) }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("X", "GALAXY")));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("X", "")));
    }

    [Fact]
    public async Task Create_RejectsADuplicateCode_IgnoringCase()
    {
        await SeedAsync("CN-HN", DepartmentLevelType.Branch);

        await Assert.ThrowsAsync<ConflictException>(() => Service().CreateDepartmentAsync(Create("cn-hn", "BRANCH")));
    }

    [Fact]
    public async Task Create_EnforcesTheLevelHierarchy()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var department = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);
        var team = await SeedAsync("T", DepartmentLevelType.Team, department.Id);
        var service = Service();

        // A branch is a root.
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("B2", "BRANCH", branch.Id)));
        // Everything else needs a parent...
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("D2", "DEPARTMENT")));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("T2", "TEAM")));
        // ...of a strictly higher level.
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("D3", "DEPARTMENT", department.Id)));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("D4", "DEPARTMENT", team.Id)));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("T3", "TEAM", team.Id)));
        // A team may also sit directly under a branch (higher level is enough).
        var result = await service.CreateDepartmentAsync(Create("T4", "TEAM", branch.Id));
        Assert.Equal(branch.Id, result.ParentDepartmentId);
    }

    [Fact]
    public async Task Create_RejectsAnUnknownOrInactiveParent()
    {
        var inactive = await SeedAsync("B-OFF", DepartmentLevelType.Branch, status: RecordStatus.Inactive);
        var service = Service();

        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateDepartmentAsync(Create("D", "DEPARTMENT", Guid.NewGuid())));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateDepartmentAsync(Create("D", "DEPARTMENT", inactive.Id)));
    }

    // ----- Update ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesTheFieldsAndTouchesUpdatedAtOnly()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var department = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);

        var result = await Service().UpdateDepartmentAsync(
            department.Id,
            Update(department, d => { d.Name = "Tên mới"; d.Description = "Mô tả"; d.Code = "d-new"; }));

        Assert.Equal("D-NEW", result.Code);
        Assert.Equal("Tên mới", result.Name);
        Assert.Equal("Mô tả", result.Description);
        Assert.Equal(Now, result.LastSyncAt);
    }

    [Fact]
    public async Task Update_KeepingTheSameCode_IsNotADuplicate()
    {
        var department = await SeedAsync("D", DepartmentLevelType.Branch);

        var result = await Service().UpdateDepartmentAsync(department.Id, Update(department, d => d.Name = "Khác"));

        Assert.Equal("D", result.Code);
    }

    [Fact]
    public async Task Update_RejectsAnotherDepartmentsCode_AndAnUnknownDepartment()
    {
        await SeedAsync("A", DepartmentLevelType.Branch);
        var b = await SeedAsync("B", DepartmentLevelType.Branch);
        var service = Service();

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateDepartmentAsync(b.Id, Update(b, d => d.Code = "a")));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateDepartmentAsync(Guid.NewGuid(), Update(b)));
    }

    [Fact]
    public async Task Update_RejectsMovingADepartmentUnderItselfOrItsDescendant()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var other = await SeedAsync("B2", DepartmentLevelType.Branch);
        var department = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);
        var team = await SeedAsync("T", DepartmentLevelType.Team, department.Id);
        var service = Service();

        // The branch cannot go under its own team: ranks reject it before the cycle check would.
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateDepartmentAsync(
            branch.Id, Update(branch, d => { d.LevelType = "DEPARTMENT"; d.ParentDepartmentId = team.Id; })));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateDepartmentAsync(
            department.Id, Update(department, d => d.ParentDepartmentId = department.Id)));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateDepartmentAsync(
            department.Id, Update(department, d => d.ParentDepartmentId = team.Id)));

        var moved = await service.UpdateDepartmentAsync(department.Id, Update(department, d => d.ParentDepartmentId = other.Id));
        Assert.Equal(other.Id, moved.ParentDepartmentId);
        Assert.Equal(["B2"], moved.Ancestors.Select(a => a.Code));
    }

    [Fact]
    public async Task Update_CannotChangeTheLevelBelowExistingChildren()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var department = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);
        await SeedAsync("T", DepartmentLevelType.Team, department.Id);

        // A DEPARTMENT with a TEAM below it cannot itself become a TEAM.
        await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateDepartmentAsync(
            department.Id, Update(department, d => d.LevelType = "TEAM")));
    }

    [Fact]
    public async Task Update_KeepingAnInactiveParent_IsAllowed_ButMovingUnderOneIsNot()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var department = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);
        var off = await SeedAsync("B-OFF", DepartmentLevelType.Branch, status: RecordStatus.Inactive);
        branch.Status = RecordStatus.Inactive;
        await _db.SaveChangesAsync();
        var service = Service();

        var renamed = await service.UpdateDepartmentAsync(department.Id, Update(department, d => d.Name = "Đổi tên"));
        Assert.Equal("Đổi tên", renamed.Name);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateDepartmentAsync(
            department.Id, Update(department, d => d.ParentDepartmentId = off.Id)));
    }

    // ----- Status ---------------------------------------------------------------------------

    [Fact]
    public async Task ChangeStatus_DeactivatesAndReactivates()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var service = Service();

        var off = await service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "inactive" });
        Assert.Equal("INACTIVE", off.Status);

        var archived = await service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "ARCHIVED" });
        Assert.Equal("ARCHIVED", archived.Status);

        var on = await service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "ACTIVE" });
        Assert.Equal("ACTIVE", on.Status);
    }

    [Fact]
    public async Task ChangeStatus_IsBlockedByActiveChildren_ButNotByInactiveOnes()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var child = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);
        var service = Service();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "INACTIVE" }));
        Assert.Contains("1 đơn vị trực thuộc", ex.Message);

        await service.ChangeStatusAsync(child.Id, new DepartmentStatusDto { Status = "INACTIVE" });
        var result = await service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "INACTIVE" });
        Assert.Equal("INACTIVE", result.Status);
    }

    [Fact]
    public async Task ChangeStatus_IsBlockedByEmployeesExceptTerminatedOnes()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var worker = await SeedEmployeeAsync(branch.Id, EmployeeStatus.OnLeave, "E1");
        await SeedEmployeeAsync(branch.Id, EmployeeStatus.Terminated, "E2");
        var service = Service();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "INACTIVE" }));
        Assert.Contains("1 nhân viên", ex.Message);

        worker.Status = EmployeeStatus.Terminated;
        await _db.SaveChangesAsync();
        var result = await service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "INACTIVE" });
        Assert.Equal("INACTIVE", result.Status);
    }

    [Fact]
    public async Task ChangeStatus_CannotReactivateUnderAnInactiveParent()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch, status: RecordStatus.Inactive);
        var child = await SeedAsync("D", DepartmentLevelType.Department, branch.Id, RecordStatus.Inactive);

        await Assert.ThrowsAsync<ValidationException>(
            () => Service().ChangeStatusAsync(child.Id, new DepartmentStatusDto { Status = "ACTIVE" }));
    }

    [Fact]
    public async Task ChangeStatus_RejectsUnknownValuesAndUnknownDepartments()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var service = Service();

        await Assert.ThrowsAsync<ValidationException>(() => service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto { Status = "GONE" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.ChangeStatusAsync(branch.Id, new DepartmentStatusDto()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ChangeStatusAsync(Guid.NewGuid(), new DepartmentStatusDto { Status = "INACTIVE" }));
    }

    // ----- Delete (soft) --------------------------------------------------------------------

    [Fact]
    public async Task Delete_ArchivesTheDepartmentInsteadOfRemovingTheRow()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var leaf = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);
        await SeedEmployeeAsync(leaf.Id, EmployeeStatus.Terminated);
        var service = Service();

        await service.DeleteDepartmentAsync(leaf.Id);

        var row = await _db.Departments.SingleAsync(d => d.Id == leaf.Id);
        Assert.Equal(RecordStatus.Archived, row.Status);
        Assert.Equal(Now, row.UpdatedAt);

        // Deleting twice is harmless.
        await service.DeleteDepartmentAsync(leaf.Id);

        // The parent may now be deleted as well, and the code stays reserved.
        await service.DeleteDepartmentAsync(branch.Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateDepartmentAsync(Create("d", "BRANCH")));
    }

    [Fact]
    public async Task Delete_HidesTheDepartmentFromTheListUnlessArchivedIsRequested()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var gone = await SeedAsync("D", DepartmentLevelType.Department, branch.Id);
        var admin = await SeedEmployeeAsync(branch.Id);
        _db.Accounts.Add(new Account { Id = _adminAccount, EmployeeId = admin.Id, Username = "a", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });
        await _db.SaveChangesAsync();
        var service = Service();

        await service.DeleteDepartmentAsync(gone.Id);

        var listed = await service.GetDepartmentsAsync(_adminAccount, new DepartmentFilterDto());
        Assert.Equal(["B"], listed.Items.Select(i => i.Code));
        var archived = await service.GetDepartmentsAsync(_adminAccount, new DepartmentFilterDto { Status = "ARCHIVED" });
        Assert.Equal(["D"], archived.Items.Select(i => i.Code));
    }

    [Fact]
    public async Task Delete_IsRefusedWhileThereAreLiveChildrenOrEmployees()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        await SeedAsync("D", DepartmentLevelType.Department, branch.Id, RecordStatus.Inactive);
        var withStaff = await SeedAsync("D2", DepartmentLevelType.Department, branch.Id);
        await SeedEmployeeAsync(withStaff.Id, EmployeeStatus.Active);
        var service = Service();

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteDepartmentAsync(branch.Id));
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteDepartmentAsync(withStaff.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteDepartmentAsync(Guid.NewGuid()));
        Assert.Equal(RecordStatus.Active, (await _db.Departments.SingleAsync(d => d.Id == withStaff.Id)).Status);
    }

    // ----- Tree -----------------------------------------------------------------------------

    [Fact]
    public async Task Tree_NestsTheDepartmentsWithCounts_OrderedByCode()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var second = await SeedAsync("B-D2", DepartmentLevelType.Department, branch.Id);
        var first = await SeedAsync("B-D1", DepartmentLevelType.Department, branch.Id);
        var team = await SeedAsync("B-D1-T", DepartmentLevelType.Team, first.Id);
        await SeedAsync("Z", DepartmentLevelType.Branch, status: RecordStatus.Inactive);
        await SeedAsync("B-D3", DepartmentLevelType.Department, branch.Id, RecordStatus.Archived);
        await SeedEmployeeAsync(first.Id, code: "E1");
        await SeedEmployeeAsync(team.Id, code: "E2");
        await SeedEmployeeAsync(team.Id, EmployeeStatus.Terminated, "E3");
        var admin = await SeedEmployeeAsync(second.Id, code: "E4");
        _db.Accounts.Add(new Account { Id = _adminAccount, EmployeeId = admin.Id, Username = "a", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });
        await _db.SaveChangesAsync();

        var tree = await Service().GetTreeAsync(_adminAccount);

        Assert.Equal(["B", "Z"], tree.Select(n => n.Code));
        var root = tree[0];
        Assert.Equal(["B-D1", "B-D2"], root.Children.Select(n => n.Code));
        Assert.Equal(3, root.EmployeeCount);
        Assert.Equal(0, root.DirectEmployeeCount);
        var d1 = root.Children[0];
        Assert.Equal(2, d1.EmployeeCount);
        Assert.Equal(1, d1.DirectEmployeeCount);
        Assert.Equal("B-D1-T", Assert.Single(d1.Children).Code);
        Assert.Empty(d1.Children[0].Children);
        Assert.Equal("INACTIVE", tree[1].Status);
        Assert.Equal("BRANCH", root.LevelType);
    }

    [Fact]
    public async Task Tree_ForADepartmentScopedCaller_StartsAtTheirOwnDepartment()
    {
        var branch = await SeedAsync("B", DepartmentLevelType.Branch);
        var department = await SeedAsync("B-D", DepartmentLevelType.Department, branch.Id);
        await SeedAsync("B-D-T", DepartmentLevelType.Team, department.Id);
        var employee = await SeedEmployeeAsync(department.Id);
        _db.Accounts.Add(new Account { Id = _adminAccount, EmployeeId = employee.Id, Username = "a", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });
        await _db.SaveChangesAsync();
        var service = new DepartmentService(_db, new AllPermissions("Department"), new FixedTime());

        var tree = await service.GetTreeAsync(_adminAccount);

        Assert.Equal("B-D", Assert.Single(tree).Code);
        Assert.Equal("B-D-T", Assert.Single(tree[0].Children).Code);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AllPermissions(string scope = "All") : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = [new UserPermissionDto("evaluation", "read", scope, "EVALUATION_READ")];
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
