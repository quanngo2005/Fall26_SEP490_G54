using System.Security.Claims;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class PermissionAuthorizationTests
{
    [Theory]
    [InlineData("All", 4)]
    [InlineData("System", 4)]
    [InlineData("*", 4)]
    [InlineData("Branch", 3)]
    [InlineData("Dept", 2)]
    [InlineData("Managed", 2)]
    [InlineData("Team", 2)]
    [InlineData("Own", 1)]
    [InlineData("Self", 1)]
    [InlineData("Unknown", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void PermissionScopes_GetScopeLevel_ReturnsCorrectHierarchy(string? scope, int expectedLevel)
    {
        var level = PermissionScopes.GetScopeLevel(scope);
        Assert.Equal(expectedLevel, level);
    }

    [Theory]
    [InlineData("All", "Own", true)]
    [InlineData("All", "Dept", true)]
    [InlineData("All", "Branch", true)]
    [InlineData("All", "All", true)]
    [InlineData("Branch", "Dept", true)]
    [InlineData("Branch", "All", false)]
    [InlineData("Dept", "Own", true)]
    [InlineData("Dept", "Dept", true)]
    [InlineData("Dept", "Branch", false)]
    [InlineData("Own", "Own", true)]
    [InlineData("Own", "Dept", false)]
    public void PermissionScopes_IsSatisfied_EvaluatesCorrectly(string granted, string required, bool expected)
    {
        var satisfied = PermissionScopes.IsSatisfied(granted, required);
        Assert.Equal(expected, satisfied);
    }

    [Fact]
    public void HasPermissionAttribute_ConstructsExpectedPolicy()
    {
        var attr = new HasPermissionAttribute("WORK", "CREATE", "Dept");
        Assert.Equal("WORK", attr.Resource);
        Assert.Equal("CREATE", attr.Action);
        Assert.Equal("Dept", attr.Scope);
        Assert.Equal("Permission:WORK:CREATE:Dept", attr.Policy);

        var defaultScopeAttr = new HasPermissionAttribute("KPI", "READ");
        Assert.Equal("Own", defaultScopeAttr.Scope);
        Assert.Equal("Permission:KPI:READ:Own", defaultScopeAttr.Policy);
    }

    [Fact]
    public void HasPermissionAttribute_ThrowsOnInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => new HasPermissionAttribute("", "READ"));
        Assert.Throws<ArgumentException>(() => new HasPermissionAttribute("WORK", ""));
        Assert.Throws<ArgumentException>(() => new HasPermissionAttribute("WORK", "READ", " "));
    }

    [Fact]
    public async Task PolicyProvider_BuildsPermissionRequirementPolicy()
    {
        var options = Options.Create(new AuthorizationOptions());
        var provider = new PermissionAuthorizationPolicyProvider(options);

        var policy = await provider.GetPolicyAsync("Permission:WORK:UPDATE:Dept");

        Assert.NotNull(policy);
        var req = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        Assert.Equal("WORK", req.Resource);
        Assert.Equal("UPDATE", req.Action);
        Assert.Equal("Dept", req.Scope);
    }

    [Fact]
    public async Task AuthorizationHandler_AdminRole_BypassesPermissionCheck()
    {
        var mockService = new FakePermissionService(false);
        var handler = new PermissionAuthorizationHandler(mockService);

        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role", "ADMIN"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var requirement = new PermissionRequirement("WORK", "DELETE", "All");
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task AuthorizationHandler_UserWithPermission_Succeeds()
    {
        var accountId = Guid.NewGuid();
        var mockService = new FakePermissionService(true);
        var handler = new PermissionAuthorizationHandler(mockService);

        var claims = new[]
        {
            new Claim("sub", accountId.ToString()),
            new Claim("role", "EMPLOYEE"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var requirement = new PermissionRequirement("WORK", "READ", "Own");
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task AuthorizationHandler_UserWithoutPermission_DoesNotSucceed()
    {
        var accountId = Guid.NewGuid();
        var mockService = new FakePermissionService(false);
        var handler = new PermissionAuthorizationHandler(mockService);

        var claims = new[]
        {
            new Claim("sub", accountId.ToString()),
            new Claim("role", "EMPLOYEE"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var requirement = new PermissionRequirement("WORK", "APPROVE", "Dept");
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task PermissionService_GetEffectivePermissions_CacheMissAndHit()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var memoryCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        using var dbContext = new AppDbContext(options);
        var accountId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = employeeId,
            EmployeeCode = "EMP001",
            FullName = "Nguyen Van A",
            Email = "vana@bank.com",
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var account = new Account
        {
            Id = accountId,
            EmployeeId = employeeId,
            Employee = employee,
            Username = "vana",
            PasswordHash = "hashed",
            IsLocked = false,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var role = new Role
        {
            Id = roleId,
            Code = "OFFICER",
            Name = "Officer",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var permission = new PermissionRecord
        {
            Id = permissionId,
            Code = "WORK_READ",
            Name = "Read Work",
            Resource = "WORK",
            Action = "READ",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var rolePermission = new RolePermissionAssignment
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            Role = role,
            PermissionId = permissionId,
            Permission = permission,
            Scope = "Own",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var userRole = new UserRole
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Account = account,
            RoleId = roleId,
            Role = role,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Employees.Add(employee);
        dbContext.Accounts.Add(account);
        dbContext.Roles.Add(role);
        dbContext.Permissions.Add(permission);
        dbContext.RolePermissions.Add(rolePermission);
        dbContext.UserRoles.Add(userRole);
        await dbContext.SaveChangesAsync();

        var service = new PermissionService(dbContext, memoryCache);

        // 1. Initial call (cache miss -> loads DB -> caches)
        var permissions = await service.GetEffectivePermissionsAsync(accountId, CancellationToken.None);
        var single = Assert.Single(permissions);
        Assert.Equal("WORK", single.Resource);
        Assert.Equal("READ", single.Action);
        Assert.Equal("Own", single.Scope);

        // Verify cache entry exists
        var cachedString = await memoryCache.GetStringAsync(PermissionService.GetCacheKey(accountId));
        Assert.NotNull(cachedString);

        // 2. Second call (cache hit)
        var cachedResult = await service.GetEffectivePermissionsAsync(accountId, CancellationToken.None);
        Assert.Single(cachedResult);

        // 3. HasPermission check
        var hasReadOwn = await service.HasPermissionAsync(accountId, "WORK", "READ", "Own", CancellationToken.None);
        Assert.True(hasReadOwn);

        var hasReadDept = await service.HasPermissionAsync(accountId, "WORK", "READ", "Dept", CancellationToken.None);
        Assert.False(hasReadDept); // Officer only has Own, Dept requires level 2

        // 4. Invalidation
        await service.InvalidateUserPermissionsAsync(accountId, CancellationToken.None);
        var afterInvalidate = await memoryCache.GetStringAsync(PermissionService.GetCacheKey(accountId));
        Assert.Null(afterInvalidate);
    }

    [Fact]
    public async Task PermissionService_InvalidateRolePermissions_PurgesAllUsersInRole()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var memoryCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        using var dbContext = new AppDbContext(options);
        var roleId = Guid.NewGuid();
        var acc1 = Guid.NewGuid();
        var acc2 = Guid.NewGuid();

        dbContext.UserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), AccountId = acc1, RoleId = roleId, UpdatedAt = DateTimeOffset.UtcNow },
            new UserRole { Id = Guid.NewGuid(), AccountId = acc2, RoleId = roleId, UpdatedAt = DateTimeOffset.UtcNow }
        );
        await dbContext.SaveChangesAsync();

        // Seed cache
        await memoryCache.SetStringAsync(PermissionService.GetCacheKey(acc1), "[]");
        await memoryCache.SetStringAsync(PermissionService.GetCacheKey(acc2), "[]");

        var service = new PermissionService(dbContext, memoryCache);
        await service.InvalidateRolePermissionsAsync(roleId, CancellationToken.None);

        Assert.Null(await memoryCache.GetStringAsync(PermissionService.GetCacheKey(acc1)));
        Assert.Null(await memoryCache.GetStringAsync(PermissionService.GetCacheKey(acc2)));
    }

    [Fact]
    public async Task MeController_GetPermissions_ReturnsSuccessResponse()
    {
        var accountId = Guid.NewGuid();
        var expectedDto = new UserPermissionsResponseDto(
            accountId,
            "test@bank.com",
            "Test User",
            ["OFFICER"],
            [new UserPermissionDto("WORK", "READ", "Own", "WORK_READ")]);

        var fakeService = new FakePermissionService(true, expectedDto);
        var controller = new MeController(fakeService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", accountId.ToString())], "TestAuth")),
                },
            },
        };

        var actionResult = await controller.GetPermissions(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<UserPermissionsResponseDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(accountId, response.Data.AccountId);
        Assert.Equal("test@bank.com", response.Data.Email);
        Assert.Single(response.Data.Permissions);
    }

    private sealed class FakePermissionService(
        bool hasPermissionResult,
        UserPermissionsResponseDto? userDto = null) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(userDto);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>(userDto?.Permissions ?? []);

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(hasPermissionResult);

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
