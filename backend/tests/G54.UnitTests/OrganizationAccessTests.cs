using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.Api.Middleware;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Department;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace G54.UnitTests;

/// <summary>
/// End-to-end HTTP checks of the role gate on the organization screens (SCR-ORG-01 departments and SCR-ORG-02 job
/// positions). The host uses the real JWT validation, the real policy/permission handlers and the real controllers;
/// only the services behind them are stubbed, so no database or Redis is needed.
/// </summary>
public sealed class OrganizationAccessTests : IAsyncLifetime
{
    private const string Issuer = "g54-tests";
    private const string Audience = "g54-tests";
    private const string Key = "test-signing-key-test-signing-key-0123456789";
    private static readonly Guid PositionId = Guid.NewGuid();
    private static readonly string[] ExpectedWorkspaces = ["admin", "branchManager", "departmentManager"];
    private static readonly string[] CoveredRoutes =
    [
        "/departments",
        "/departments/tree",
        "/departments/5eed0000-0000-0000-0001-000000000001",
        "/job-positions",
        "/job-positions/5eed0000-0000-0000-0006-000000000001",
    ];

    private IHost _host = null!;
    private HttpClient _client = null!;
    private JwtTokenProvider _tokens = null!;
    private FakePermissions _permissions = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:Key"] = Key,
            })
            .Build();
        _tokens = new JwtTokenProvider(configuration);
        _permissions = new FakePermissions();

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(DepartmentsController).Assembly);
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddSingleton(_tokens);
                    services.AddSingleton<IPermissionService>(_permissions);
                    services.AddSingleton<IDepartmentService, StubDepartmentService>();
                    services.AddSingleton<IJobPositionService, StubJobPositionService>();
                    services.AddExceptionHandler<GlobalExceptionHandler>();
                    services.AddProblemDetails();
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                    {
                        options.MapInboundClaims = false;
                        options.TokenValidationParameters = _tokens.CreateValidationParameters();
                        options.Events = new JwtBearerEvents
                        {
                            OnChallenge = async context =>
                            {
                                context.HandleResponse();
                                await ApiErrorResponses.WriteAsync(
                                    context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
                            },
                            OnForbidden = context => ApiErrorResponses.WriteAsync(
                                context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden),
                        };
                    });
                    services.AddAuthorization(options =>
                    {
                        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                        options.AddOrganizationPolicy();
                    });
                    services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
                    services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
                })
                .Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    public static TheoryData<string> AllowedRoles => new()
    {
        "SYSTEM_ADMIN",
        "ADMIN",
        "BRANCH_MANAGER",
        "DEPT_MANAGER",
        "DEPARTMENT_MANAGER",
        "MANAGER",
        "branch-manager",
        " Dept Manager ",
    };

    public static TheoryData<string> BlockedRoles => new()
    {
        "EMPLOYEE",
        "HR",
        "HR_MANAGER",
        "QA",
        "VERIFIER",
        "COMPLIANCE",
        "AUDITOR",
        "MANAGER_ASSISTANT",
        "",
    };

    [Theory]
    [MemberData(nameof(AllowedRoles))]
    public async Task EveryOrganizationEndpointIsOpenToTheThreeAllowedRoles(string role)
    {
        _permissions.GrantAll = true;

        foreach (var route in CoveredRoutes)
        {
            var response = await GetAsync(route, role);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} on {route} -> {(int)response.StatusCode}");
        }
    }

    [Theory]
    [MemberData(nameof(BlockedRoles))]
    public async Task EveryOtherRoleIsForbiddenEvenWhenItHoldsTheDataPermission(string role)
    {
        _permissions.GrantAll = true; // proves the role gate, not the permission, is what blocks the request

        foreach (var route in CoveredRoutes)
        {
            var response = await GetAsync(route, role);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{role} on {route} -> {(int)response.StatusCode}");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task AUserWithSeveralRolesPassesWhenAnyOfThemIsAllowed()
    {
        _permissions.GrantAll = true;

        var response = await GetAsync("/job-positions", "EMPLOYEE", "VERIFIER", "BRANCH_MANAGER");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnAllowedRoleStillNeedsTheDepartmentDataPermission()
    {
        _permissions.GrantAll = false;

        var departments = await GetAsync("/departments", "BRANCH_MANAGER");
        var jobPositions = await GetAsync("/job-positions", "BRANCH_MANAGER");

        Assert.Equal(HttpStatusCode.Forbidden, departments.StatusCode); // existing HasPermission is kept
        Assert.Equal(HttpStatusCode.OK, jobPositions.StatusCode); // job positions are gated by role only
    }

    [Fact]
    public async Task AnAnonymousOrInvalidTokenIsUnauthorizedNotForbidden()
    {
        foreach (var route in CoveredRoutes)
        {
            var anonymous = await _client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/job-positions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task AnUnknownJobPositionIsANotFoundEnvelopeForAnAllowedRole()
    {
        var response = await GetAsync($"/job-positions/{Guid.NewGuid()}", "SYSTEM_ADMIN");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task ABadStatusFilterIsAValidationErrorForAnAllowedRole()
    {
        var response = await GetAsync("/job-positions?status=BOGUS", "DEPT_MANAGER");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void TheRoleSetMatchesTheSidebarRoleMapping()
    {
        // Every allowed code must map to a workspace role the sidebar understands, and nothing else may be allowed.
        var allowedWorkspaces = RoleCodes.OrganizationViewers
            .Select(SidebarCatalog.MapRole)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedWorkspaces, allowedWorkspaces.OfType<string>().ToArray());
        Assert.All(["EMPLOYEE", "HR", "QA", "VERIFIER"], role => Assert.DoesNotContain(role, RoleCodes.OrganizationViewers));
    }

    private Task<HttpResponseMessage> GetAsync(string route, params string[] roles)
    {
        var token = _tokens.CreateAccessToken(
            Guid.NewGuid(), "user@test.local", "User", roles, [], TimeSpan.FromMinutes(5));
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return _client.SendAsync(request);
    }

    private sealed class FakePermissions : IPermissionService
    {
        public bool GrantAll { get; set; }

        public Task<G54.BLL.Dtos.Permission.UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<G54.BLL.Dtos.Permission.UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<G54.BLL.Dtos.Permission.UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<G54.BLL.Dtos.Permission.UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(GrantAll);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubDepartmentService : IDepartmentService
    {
        private static readonly DepartmentDetailDto Detail = new(
            Guid.NewGuid(), "D", "D", null, "DEPARTMENT", "ACTIVE", null, [], null, 0, 0, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        public Task<PagedResult<DepartmentSummaryDto>> GetDepartmentsAsync(Guid accountId, DepartmentFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<DepartmentSummaryDto>([], 0, 1, 20));

        public Task<IReadOnlyList<DepartmentTreeNodeDto>> GetTreeAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DepartmentTreeNodeDto>>([]);

        public Task<DepartmentDetailDto> GetDepartmentAsync(Guid accountId, Guid departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail);

        public Task<DepartmentOverviewDto> GetOverviewAsync(Guid accountId, Guid departmentId, Guid? periodId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<DepartmentMemberDto>> GetMembersAsync(Guid accountId, Guid departmentId, DepartmentMemberFilterDto filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DepartmentDetailDto> CreateDepartmentAsync(CreateDepartmentDto dto, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DepartmentDetailDto> UpdateDepartmentAsync(Guid id, UpdateDepartmentDto dto, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DepartmentDetailDto> ChangeStatusAsync(Guid id, DepartmentStatusDto dto, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteDepartmentAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubJobPositionService : IJobPositionService
    {
        public Task<PagedResult<JobPositionSummaryDto>> GetJobPositionsAsync(JobPositionFilterDto filter, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status == "BOGUS")
            {
                throw new G54.BLL.Common.Exceptions.ValidationException("status");
            }

            return Task.FromResult(new PagedResult<JobPositionSummaryDto>([], 0, 1, 50));
        }

        public Task<JobPositionDetailDto?> GetJobPositionByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobPositionDetailDto?>(id == Guid.Parse("5eed0000-0000-0000-0006-000000000001")
                ? new JobPositionDetailDto(PositionId, "TELLER", "Teller", null, 0, DateTimeOffset.UnixEpoch, "ACTIVE", null, [])
                : null);
    }
}
