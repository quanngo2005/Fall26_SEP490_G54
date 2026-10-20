using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Sidebar;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class SidebarConfigurationServiceTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Guid> AddAccountAsync(AppDbContext db, string roleCode)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Username = Guid.NewGuid().ToString(),
            PasswordHash = "test-hash",
            Employee = new Employee { Id = Guid.NewGuid(), FullName = "Test", Email = "test@example.test", EmployeeCode = Guid.NewGuid().ToString(), Status = EmployeeStatus.Active },
        };
        account.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            Account = account,
            Role = new Role { Id = Guid.NewGuid(), Code = roleCode, Name = roleCode },
        });
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task OnlyAdminCanListAllOrSaveConfigurations()
    {
        await using var db = CreateContext();
        var employee = await AddAccountAsync(db, "EMPLOYEE");
        var service = new SidebarConfigurationService(db);
        var own = await service.GetAsync(employee, false, CancellationToken.None);
        Assert.Equal("employee", Assert.Single(own).Role);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(employee, true, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SaveAsync(employee, "employee", new SaveSidebarConfigurationDto { ItemIds = ["overview"] }, CancellationToken.None));
    }

    [Fact]
    public async Task SavedOrderIsSharedWithRoleAndStaleSaveFails()
    {
        await using var db = CreateContext();
        var admin = await AddAccountAsync(db, "ADMIN");
        var employee = await AddAccountAsync(db, "EMPLOYEE");
        var service = new SidebarConfigurationService(db);
        var saved = await service.SaveAsync(admin, "employee", new SaveSidebarConfigurationDto { ItemIds = ["myProfile", "overview"] }, CancellationToken.None);
        Assert.Equal(1, saved.Version);
        Assert.Equal(saved.ItemIds, Assert.Single(await service.GetAsync(employee, false, CancellationToken.None)).ItemIds);
        await Assert.ThrowsAsync<ConflictException>(() => service.SaveAsync(admin, "employee", new SaveSidebarConfigurationDto { ItemIds = ["overview"], Version = 0 }, CancellationToken.None));
    }

    [Theory]
    [InlineData("employee", "unknown")]
    [InlineData("employee", "departments")]
    [InlineData("admin", "myProfile")]
    public async Task InvalidMenusCannotBeSaved(string role, string extra)
    {
        await using var db = CreateContext();
        var admin = await AddAccountAsync(db, "ADMIN");
        var service = new SidebarConfigurationService(db);
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(admin, role, new SaveSidebarConfigurationDto { ItemIds = ["overview", extra] }, CancellationToken.None));
        Assert.Empty(db.SidebarConfigurations);
    }

    [Fact]
    public async Task LockedAccountCannotReadOrSave()
    {
        await using var db = CreateContext();
        var admin = await AddAccountAsync(db, "ADMIN");
        (await db.Accounts.SingleAsync()).IsLocked = true;
        await db.SaveChangesAsync();
        var service = new SidebarConfigurationService(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(admin, true, CancellationToken.None));
    }

    [Fact]
    public async Task RoutesAreSharedAndPreservedByOlderClients()
    {
        await using var db = CreateContext();
        var admin = await AddAccountAsync(db, "ADMIN");
        var employee = await AddAccountAsync(db, "EMPLOYEE");
        var service = new SidebarConfigurationService(db);
        var request = new SaveSidebarConfigurationDto
        {
            ItemIds = ["overview", "myProfile"],
            Routes = new() { ["myProfile"] = "/kpi-definitions" },
        };
        await service.SaveAsync(admin, "employee", request, CancellationToken.None);
        var shared = Assert.Single(await service.GetAsync(employee, false, CancellationToken.None));
        Assert.Equal("/kpi-definitions", shared.Routes!["myProfile"]);
        var saved = await service.SaveAsync(admin, "employee", new SaveSidebarConfigurationDto { ItemIds = ["myProfile", "overview"], Version = 1 }, CancellationToken.None);
        Assert.Equal("/kpi-definitions", saved.Routes!["myProfile"]);
        saved = await service.SaveAsync(admin, "employee", new SaveSidebarConfigurationDto { ItemIds = ["overview"], Version = 2 }, CancellationToken.None);
        Assert.Empty(saved.Routes!);
    }

    [Theory]
    [InlineData("employee", "myProfile", "https://example.test")]
    [InlineData("employee", "myProfile", "//example.test")]
    [InlineData("employee", "myProfile", "/not-a-page")]
    [InlineData("employee", "myProfile", "/sidebar-settings")]
    [InlineData("employee", "organization", "/home")]
    [InlineData("employee", "notifications", "/home")]
    [InlineData("admin", "systemConfiguration", "")]
    public async Task InvalidOrUnreachableRoutesAreRejected(string role, string item, string route)
    {
        await using var db = CreateContext();
        var admin = await AddAccountAsync(db, "ADMIN");
        var service = new SidebarConfigurationService(db);
        var request = new SaveSidebarConfigurationDto
        {
            ItemIds = ["overview", "myProfile", "organization", "systemConfiguration"],
            Routes = new() { [item] = route },
        };
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(admin, role, request, CancellationToken.None));
        Assert.Empty(db.SidebarConfigurations);
    }
}
