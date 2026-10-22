using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.PerformanceDashboard;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace G54.UnitTests;

public sealed class PerformanceDashboardControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static PerformanceDashboardController CreateController(RecordingService service, Guid? accountId = null)
    {
        var claims = accountId.HasValue ? new[] { new Claim("sub", accountId.Value.ToString()) } : [];
        return new PerformanceDashboardController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-1",
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, accountId.HasValue ? "test" : null)),
                },
            },
        };
    }

    // ----- Routes and policies --------------------------------------------------------------

    [Fact]
    public void Controller_UsesTheCanonicalRoot()
    {
        Assert.Equal(
            "performance-dashboard",
            typeof(PerformanceDashboardController).GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Theory]
    [InlineData(nameof(PerformanceDashboardController.GetMine), "me")]
    [InlineData(nameof(PerformanceDashboardController.GetEmployee), "employees/{employeeId:guid}")]
    [InlineData(nameof(PerformanceDashboardController.GetDepartment), "departments/{departmentId:guid}")]
    [InlineData(nameof(PerformanceDashboardController.GetBranch), "branches/{branchId:guid}")]
    public void EveryAction_IsAGetWithTheDocumentedRoute(string action, string template)
    {
        var method = typeof(PerformanceDashboardController).GetMethod(action);

        Assert.NotNull(method);
        Assert.Equal(template, method.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    [Fact]
    public void TheDashboardIsReadOnly()
    {
        var verbs = typeof(PerformanceDashboardController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(a => a.HttpMethods)
            .Distinct()
            .ToArray();

        Assert.Equal(["GET"], verbs);
    }

    [Fact]
    public void Controller_DefaultsToTheOwnScope_SoAnEmployeeCanReachTheirOwnDashboard()
    {
        var permission = Assert.Single(typeof(PerformanceDashboardController).GetCustomAttributes<HasPermissionAttribute>());

        Assert.Equal("evaluation", permission.Resource);
        Assert.Equal("read", permission.Action);
        Assert.Equal("Own", permission.Scope);
    }

    [Theory]
    [InlineData(nameof(PerformanceDashboardController.GetDepartment))]
    [InlineData(nameof(PerformanceDashboardController.GetBranch))]
    public void UnitDashboards_RequireAtLeastTheDepartmentScope(string action)
    {
        var permission = Assert.Single(
            typeof(PerformanceDashboardController).GetMethod(action)!.GetCustomAttributes<HasPermissionAttribute>());

        Assert.Equal("evaluation", permission.Resource);
        Assert.Equal("read", permission.Action);
        Assert.Equal("Dept", permission.Scope);
    }

    [Theory]
    [InlineData(nameof(PerformanceDashboardController.GetMine))]
    [InlineData(nameof(PerformanceDashboardController.GetEmployee))]
    public void PersonalDashboards_DoNotRaiseTheRequiredScopeAboveOwn(string action)
    {
        var onAction = typeof(PerformanceDashboardController).GetMethod(action)!.GetCustomAttributes<HasPermissionAttribute>();

        Assert.Empty(onAction);
    }

    [Fact]
    public void TheMeRouteTakesNoEmployeeId_SoItCannotReadAPeer()
    {
        var parameters = typeof(PerformanceDashboardController).GetMethod(nameof(PerformanceDashboardController.GetMine))!
            .GetParameters()
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain("employeeId", parameters);
        Assert.Contains("periodId", parameters);
    }

    // ----- Behaviour ------------------------------------------------------------------------

    [Fact]
    public async Task GetMine_UsesTheAuthenticatedSubjectAndTheCanonicalEnvelope()
    {
        var accountId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service, accountId);

        var result = await controller.GetMine(periodId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(accountId, service.AccountId);
        Assert.Equal(periodId, service.PeriodId);
        Assert.Equal("me", service.LastCall);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(200, root.GetProperty("status").GetInt32());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
        Assert.Equal("Nhân viên", root.GetProperty("data").GetProperty("employee").GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").GetProperty("totalScore").ValueKind);
    }

    [Fact]
    public async Task GetEmployee_PassesTheRouteIdAndTheCallerSeparately()
    {
        var accountId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service, accountId);

        await controller.GetEmployee(employeeId, null, CancellationToken.None);

        Assert.Equal(accountId, service.AccountId);
        Assert.Equal(employeeId, service.TargetId);
        Assert.Null(service.PeriodId);
        Assert.Equal("employee", service.LastCall);
    }

    [Fact]
    public async Task GetDepartment_PassesTheRouteIdAndTheCaller()
    {
        var accountId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service, accountId);

        var result = await controller.GetDepartment(departmentId, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(accountId, service.AccountId);
        Assert.Equal(departmentId, service.TargetId);
        Assert.Equal("department", service.LastCall);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        Assert.Equal("DEPARTMENT", document.RootElement.GetProperty("data").GetProperty("orgUnit").GetProperty("levelType").GetString());
    }

    [Fact]
    public async Task GetBranch_PassesTheRouteIdAndTheCaller()
    {
        var accountId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = CreateController(service, accountId);

        await controller.GetBranch(branchId, null, CancellationToken.None);

        Assert.Equal(accountId, service.AccountId);
        Assert.Equal(branchId, service.TargetId);
        Assert.Equal("branch", service.LastCall);
    }

    [Fact]
    public async Task EveryAction_WithoutAnIdentity_NeverReachesTheService()
    {
        var service = new RecordingService();
        var controller = CreateController(service);

        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetMine(null, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetEmployee(Guid.NewGuid(), null, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetDepartment(Guid.NewGuid(), null, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetBranch(Guid.NewGuid(), null, CancellationToken.None));

        Assert.Null(service.LastCall);
    }

    private sealed class RecordingService : IPerformanceDashboardService
    {
        public Guid AccountId { get; private set; }
        public Guid TargetId { get; private set; }
        public Guid? PeriodId { get; private set; }
        public string? LastCall { get; private set; }

        public Task<PersonalPerformanceDashboardDto> GetMyDashboardAsync(
            Guid accountId,
            Guid? periodId,
            CancellationToken cancellationToken = default)
        {
            Record("me", accountId, Guid.Empty, periodId);
            return Task.FromResult(Personal());
        }

        public Task<PersonalPerformanceDashboardDto> GetEmployeeDashboardAsync(
            Guid accountId,
            Guid employeeId,
            Guid? periodId,
            CancellationToken cancellationToken = default)
        {
            Record("employee", accountId, employeeId, periodId);
            return Task.FromResult(Personal());
        }

        public Task<UnitPerformanceDashboardDto> GetDepartmentDashboardAsync(
            Guid accountId,
            Guid departmentId,
            Guid? periodId,
            CancellationToken cancellationToken = default)
        {
            Record("department", accountId, departmentId, periodId);
            return Task.FromResult(Unit("DEPARTMENT"));
        }

        public Task<UnitPerformanceDashboardDto> GetBranchDashboardAsync(
            Guid accountId,
            Guid branchId,
            Guid? periodId,
            CancellationToken cancellationToken = default)
        {
            Record("branch", accountId, branchId, periodId);
            return Task.FromResult(Unit("BRANCH"));
        }

        private void Record(string call, Guid accountId, Guid targetId, Guid? periodId)
        {
            LastCall = call;
            AccountId = accountId;
            TargetId = targetId;
            PeriodId = periodId;
        }

        private static PersonalPerformanceDashboardDto Personal() => new(
            new DashboardEmployeeDto(Guid.NewGuid(), "E001", "Nhân viên", Guid.NewGuid(), "Phòng", "Chuyên viên"),
            null,
            [],
            null,
            null,
            null,
            null,
            null,
            [],
            DateTimeOffset.UnixEpoch);

        private static UnitPerformanceDashboardDto Unit(string level) => new(
            new DashboardOrgUnitDto(Guid.NewGuid(), "CODE", "Name", level),
            null,
            [],
            0,
            0,
            null,
            null,
            null,
            [],
            [],
            [],
            [],
            DateTimeOffset.UnixEpoch);
    }
}
