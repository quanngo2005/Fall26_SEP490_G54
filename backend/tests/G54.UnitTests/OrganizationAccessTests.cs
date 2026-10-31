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
using G54.BLL.Dtos.Permission;
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
/// End-to-end HTTP checks of who may open the organization screens (SCR-ORG-01 departments, SCR-ORG-02 job positions).
/// Access is decided by the <c>organization:read</c> permission, which the <c>SeedOrganizationReadAccess</c> migration
/// grants to the system admin (All), branch manager (Branch) and department manager (Dept) only. The host uses the real
/// JWT validation, the real <c>[HasPermission]</c> policy provider and handler, and the real controllers; the services
/// behind them are stubbed and the role to permission lookup is a table that mirrors the migration, so no database or
/// Redis is needed. <see cref="OrganizationAccessMigrationPostgresTests"/> proves the table matches the real database.
/// </summary>
public sealed class OrganizationAccessTests : IAsyncLifetime
{
    private const string Issuer = "g54-tests";
    private const string Audience = "g54-tests";
    private const string Key = "test-signing-key-test-signing-key-0123456789";
    private static readonly Guid PositionId = Guid.Parse("5eed0000-0000-0000-0006-000000000001");
    private static readonly string[] ExpectedPermissionGrantedRoles = ["SYSTEM_ADMIN", "BRANCH_MANAGER", "DEPT_MANAGER", "MANAGER"];
    private static readonly string[] CoveredRoutes =
    [
        "/departments",
        "/departments/tree",
        "/departments/5eed0000-0000-0000-0001-000000000001",
        "/job-positions",
        "/job-positions/5eed0000-0000-0000-0006-000000000001",
    ];

    /// <summary>What <c>role_permission</c> holds for <c>organization:read</c> after the migration (role, scope).</summary>
    private static readonly Dictionary<string, string> OrganizationRead = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SYSTEM_ADMIN"] = "All",
        ["BRANCH_MANAGER"] = "Branch",
        ["DEPT_MANAGER"] = "Dept",
        ["MANAGER"] = "Dept",
    };

    private IHost _host = null!;
    private HttpClient _client = null!;
    private JwtTokenProvider _tokens = null!;

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

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(DepartmentsController).Assembly);
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddSingleton(_tokens);
                    services.AddSingleton<IPermissionService, RolePermissionTable>();
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
                        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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
        "BRANCH_MANAGER",
        "DEPT_MANAGER",
        "MANAGER",
        "ADMIN",
    };

    public static TheoryData<string> BlockedRoles => new()
    {
        "EMPLOYEE",
        "VERIFIER",
        "HR",
        "HR_MANAGER",
        "QA",
        "COMPLIANCE",
        "AUDITOR",
        "",
    };

    [Theory]
    [MemberData(nameof(AllowedRoles))]
    public async Task EveryOrganizationEndpointIsOpenToTheRolesGrantedOrganizationRead(string role)
    {
        foreach (var route in CoveredRoutes)
        {
            var response = await GetAsync(route, role);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} on {route} -> {(int)response.StatusCode}");
        }
    }

    [Theory]
    [MemberData(nameof(BlockedRoles))]
    public async Task EveryRoleWithoutOrganizationReadIsForbidden(string role)
    {
        foreach (var route in CoveredRoutes)
        {
            var response = await GetAsync(route, role);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{role} on {route} -> {(int)response.StatusCode}");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(403, body.RootElement.GetProperty("status").GetInt32());
        }
    }

    [Fact]
    public async Task HoldingTheOldEvaluationReadPermissionIsNoLongerEnough()
    {
        // The departments screen used to borrow evaluation:read; a role that only has that must now be rejected.
        RolePermissionTable.ExtraGrants["EVALUATOR"] = [new UserPermissionDto("evaluation", "read", "All", "EVALUATION_READ")];

        foreach (var route in CoveredRoutes)
        {
            var response = await GetAsync(route, "EVALUATOR");

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"EVALUATOR on {route} -> {(int)response.StatusCode}");
        }

        RolePermissionTable.ExtraGrants.Remove("EVALUATOR");
    }

    [Fact]
    public async Task AUserWithSeveralRolesPassesWhenAnyOfThemHoldsThePermission()
    {
        var response = await GetAsync("/job-positions", "EMPLOYEE", "VERIFIER", "BRANCH_MANAGER");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ARoleThatIsGrantedLaterGetsAccessWithoutAnyCodeChange()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/job-positions", "HR")).StatusCode);

        RolePermissionTable.ExtraGrants["HR"] = [new UserPermissionDto("organization", "read", "Dept", "ORGANIZATION_READ")];
        var granted = await GetAsync("/job-positions", "HR");
        RolePermissionTable.ExtraGrants.Remove("HR");

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
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
    public async Task AnUnknownJobPositionIsANotFoundEnvelopeForAGrantedRole()
    {
        var response = await GetAsync($"/job-positions/{Guid.NewGuid()}", "SYSTEM_ADMIN");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task ABadStatusFilterIsAValidationErrorForAGrantedRole()
    {
        var response = await GetAsync("/job-positions?status=BOGUS", "DEPT_MANAGER");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void TheMigrationGrantsExactlyTheRolesThisTestAssumes()
    {
        Assert.Equal(ExpectedPermissionGrantedRoles.Order(StringComparer.Ordinal), OrganizationRead.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Organization", nameof(OrganizationAccessCodes).Replace("AccessCodes", string.Empty));
        Assert.Equal(("organization", "read", "ORGANIZATION_READ"), (OrganizationAccessCodes.Resource, OrganizationAccessCodes.Read, OrganizationAccessCodes.Code));
    }

    private Task<HttpResponseMessage> GetAsync(string route, params string[] roles)
    {
        var accountId = Guid.NewGuid();
        RolePermissionTable.Accounts[accountId] = roles; // the table resolves permissions from the account's roles
        var token = _tokens.CreateAccessToken(accountId, "user@test.local", "User", roles, [], TimeSpan.FromMinutes(5));
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return _client.SendAsync(request);
    }

    /// <summary>
    /// Stands in for <c>role_permission</c> + <c>user_role</c>: each request uses its own account id, and this table maps the
    /// account to its roles and the roles to the grants the migration creates (plus per-test extras).
    /// </summary>
    private sealed class RolePermissionTable : IPermissionService
    {
        public static Dictionary<string, UserPermissionDto[]> ExtraGrants { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<Guid, string[]> Accounts { get; } = [];

        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(RolesOf(id).SelectMany(Grants).Any(grant =>
                grant.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase)
                && grant.Action.Equals(action, StringComparison.OrdinalIgnoreCase)
                && PermissionScopes.IsSatisfied(grant.Scope, requiredScope)));

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        private static string[] RolesOf(Guid id) => Accounts.TryGetValue(id, out var roles) ? roles : [];

        private static IEnumerable<UserPermissionDto> Grants(string role)
        {
            if (OrganizationRead.TryGetValue(role, out var scope))
            {
                yield return new UserPermissionDto("organization", "read", scope, "ORGANIZATION_READ");
            }

            if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                yield return new UserPermissionDto("*", "*", PermissionScopes.All, "FULL_ACCESS");
            }

            if (ExtraGrants.TryGetValue(role, out var extra))
            {
                foreach (var grant in extra)
                {
                    yield return grant;
                }
            }
        }
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
            if (filter.Status == "BOGUS")
            {
                throw new G54.BLL.Common.Exceptions.ValidationException("status");
            }

            return Task.FromResult(new PagedResult<JobPositionSummaryDto>([], 0, 1, 50));
        }

        public Task<JobPositionDetailDto?> GetJobPositionByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobPositionDetailDto?>(id == PositionId
                ? new JobPositionDetailDto(PositionId, "TELLER", "Teller", null, 0, DateTimeOffset.UnixEpoch, "ACTIVE", null, [])
                : null);

        public Task<IReadOnlyList<JobPositionWorkTypeDto>> GetWorkTypesAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPositionDetailDto> AssignWorkTypeAsync(
            Guid id,
            AssignJobPositionWorkTypeDto request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPositionDetailDto> RemoveWorkTypeAsync(Guid id, Guid workTypeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
