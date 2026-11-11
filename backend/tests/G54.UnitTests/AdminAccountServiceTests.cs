using G54.BLL.Dtos.Admin;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class AdminAccountServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Department _dept = new()
    {
        Id = Guid.NewGuid(),
        Code = "SALES",
        Name = "Sales",
        LevelType = DepartmentLevelType.Department,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private readonly Department _otherDept = new()
    {
        Id = Guid.NewGuid(),
        Code = "OPS",
        Name = "Operations",
        LevelType = DepartmentLevelType.Department,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private readonly JobPosition _position = new()
    {
        Id = Guid.NewGuid(),
        Code = "AE",
        Title = "Account Executive",
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private readonly Role _admin = new()
    {
        Id = Guid.NewGuid(),
        Code = "ADMIN",
        Name = "Administrator",
        Status = RecordStatus.Active,
        UpdatedAt = Now,
    };

    private readonly Role _staff = new()
    {
        Id = Guid.NewGuid(),
        Code = "STAFF",
        Name = "Staff",
        Description = "Regular staff",
        Status = RecordStatus.Inactive,
        UpdatedAt = Now,
    };

    public void Dispose() => _db.Dispose();

    private AdminAccountService Service() => new(_db);

    private Account AddAccount(string code, string name, string username, Department dept, Role? role,
        bool locked = false, EmployeeStatus status = EmployeeStatus.Active)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = name,
            Email = $"{username}@x.local",
            DepartmentId = dept.Id,
            Department = dept,
            JobPositionId = _position.Id,
            JobPosition = _position,
            Status = status,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Employee = employee,
            Username = username,
            PasswordHash = "x",
            IsLocked = locked,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        if (role is not null)
        {
            account.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Account = account,
                RoleId = role.Id,
                Role = role,
                UpdatedAt = Now,
            });
        }

        _db.Accounts.Add(account);
        return account;
    }

    private async Task<AdminAccountService> SeedAsync()
    {
        _db.AddRange(_dept, _otherDept, _position, _admin, _staff);
        AddAccount("E001", "Alice Admin", "alice", _dept, _admin);
        AddAccount("E002", "Bob Staff", "bob", _dept, _staff, locked: true);
        AddAccount("E003", "Carol Ops", "carol", _otherDept, _staff);
        AddAccount("E004", "Dan Gone", "dan", _otherDept, null, status: EmployeeStatus.Terminated);
        await _db.SaveChangesAsync();
        return Service();
    }

    [Fact]
    public async Task GetRolesReturnsRolesOrderedByNameWithAccountCountAndSystemFlag()
    {
        var service = await SeedAsync();

        var roles = await service.GetRolesAsync();

        Assert.Equal(["Administrator", "Staff"], roles.Select(r => r.Name));
        var admin = roles[0];
        Assert.True(admin.IsSystem);
        Assert.Equal(1, admin.AccountCount);
        Assert.Equal("Active", admin.Status);
        var staff = roles[1];
        Assert.False(staff.IsSystem);
        Assert.Equal(2, staff.AccountCount);
        Assert.Equal("Inactive", staff.Status);
        Assert.Equal("Regular staff", staff.Description);
    }

    [Fact]
    public async Task GetAccountsReturnsAllOrderedByEmployeeCodeWithDerivedStatus()
    {
        var service = await SeedAsync();

        var result = await service.GetAccountsAsync(new UserAccountFilterDto());

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(["E001", "E002", "E003", "E004"], result.Items.Select(i => i.EmployeeCode));
        Assert.Equal(["ACTIVE", "LOCKED", "ACTIVE", "EMPLOYEE_TERMINATED"], result.Items.Select(i => i.Status));
        var alice = result.Items[0];
        Assert.Equal("alice", alice.Username);
        Assert.Equal("SALES", alice.DepartmentCode);
        Assert.Equal("Account Executive", alice.JobPositionTitle);
        Assert.Equal(_admin.Id, alice.RoleId);
        Assert.Equal("Administrator", alice.RoleName);
        Assert.Null(alice.LastLoginAt);
    }

    // The keyword filter uses PostgreSQL ILike, so it is covered against a real database in AdminAccountsHttpTests.
    [Fact]
    public async Task AccountWithoutRoleGetsEmptyRole()
    {
        var service = await SeedAsync();

        var result = await service.GetAccountsAsync(new UserAccountFilterDto(Status: "EMPLOYEE_TERMINATED"));

        var dan = Assert.Single(result.Items);
        Assert.Equal(Guid.Empty, dan.RoleId);
        Assert.Equal(string.Empty, dan.RoleName);
    }
    [Fact]
    public async Task RoleAndDepartmentFiltersNarrowTheResult()
    {
        var service = await SeedAsync();

        var byRole = await service.GetAccountsAsync(new UserAccountFilterDto(RoleId: _staff.Id));
        var byDept = await service.GetAccountsAsync(new UserAccountFilterDto(DepartmentId: _otherDept.Id));

        Assert.Equal(["E002", "E003"], byRole.Items.Select(i => i.EmployeeCode));
        Assert.Equal(["E003", "E004"], byDept.Items.Select(i => i.EmployeeCode));
    }

    [Theory]
    [InlineData("locked", new[] { "E002" })]
    [InlineData("ACTIVE", new[] { "E001", "E003" })]
    [InlineData("employee_terminated", new[] { "E004" })]
    [InlineData("bogus", new string[0])]
    public async Task StatusFilterMapsToAccountState(string status, string[] expected)
    {
        var service = await SeedAsync();

        var result = await service.GetAccountsAsync(new UserAccountFilterDto(Status: status));

        Assert.Equal(expected, result.Items.Select(i => i.EmployeeCode));
    }

    [Fact]
    public async Task PagingIsClampedAndSkipsEarlierPages()
    {
        var service = await SeedAsync();

        var page2 = await service.GetAccountsAsync(new UserAccountFilterDto(Page: 2, PageSize: 3));
        var clamped = await service.GetAccountsAsync(new UserAccountFilterDto(Page: 0, PageSize: 1000));

        Assert.Equal(4, page2.TotalCount);
        Assert.Equal(["E004"], page2.Items.Select(i => i.EmployeeCode));
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
        Assert.Equal(4, clamped.Items.Count);
    }
}
