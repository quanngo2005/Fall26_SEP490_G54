using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace G54.UnitTests;

/// <summary>Exercises the real API pipeline, JWT and permission service for GET /roles and GET /user-accounts.</summary>
public sealed class AdminAccountsHttpTests : IAsyncLifetime, IAsyncDisposable
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private System.Diagnostics.Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _admin = null!;
    private Account _outsider = null!;
    private Role _staffRole = null!;
    private Department _otherDepartment = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_adminacct_http_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDataAccess(new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString);
        // Each test owns a unique data source; do not retain its EF provider in the process-wide cache.
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddScoped(provider => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .EnableServiceProviderCaching(false).Options);
        _provider = services.BuildServiceProvider();
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var address = new Uri($"http://127.0.0.1:{port}");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "G54.Api.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = address.ToString();
        start.Environment["ConnectionStrings__Postgres"] = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        start.Environment["ConnectionStrings__Redis"] = redis;
        start.Environment["Jwt__Key"] = _jwtKey;
        start.Environment["Jwt__Issuer"] = "AdminAccountsHttpTests";
        start.Environment["Jwt__Audience"] = "AdminAccountsHttpTests";
        _api = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start the test API.");
        _api.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _api.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        _api.BeginOutputReadLine();
        _api.BeginErrorReadLine();
        _client = new HttpClient { BaseAddress = address };
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (_api.HasExited)
            {
                throw new InvalidOperationException($"Test API exited with {_api.ExitCode}.");
            }

            try
            {
                using var ready = await _client.GetAsync("/health/ready");
                if (ready.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel has not bound its port yet; retry until the readiness deadline.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Test API did not become ready.");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_api is not null)
        {
            if (!_api.HasExited)
            {
                _api.Kill(entireProcessTree: true);
                await _api.WaitForExitAsync();
            }

            _api.Dispose();
        }

        if (_provider is null)
        {
            return;
        }

        await _provider.DisposeAsync();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData("/roles")]
    [InlineData("/user-accounts")]
    public async Task Endpoints_WithoutAuthentication_Return401(string path)
    {
        using var response = await _client.GetAsync(path);
        await CheckAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData("/roles")]
    [InlineData("/user-accounts")]
    public async Task Endpoints_WithoutRoleUpdatePermission_Return403(string path)
    {
        Authenticate(_outsider);
        using var response = await _client.GetAsync(path);
        await CheckAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task GetRoles_ReturnsRolesInsideCamelCaseData()
    {
        Authenticate(_admin);
        using var response = await _client.GetAsync("/roles");
        var body = await CheckAsync(response, HttpStatusCode.OK);
        var roles = body.GetProperty("data").EnumerateArray().ToList();
        // The migrations seed their own roles, so assert ordering and the roles this test created.
        var names = roles.Select(r => r.GetProperty("name").GetString()!).ToList();
        Assert.Contains("Administrator", names);
        var staff = roles.Single(r => r.GetProperty("code").GetString() == "STAFF");
        Assert.Equal(1, staff.GetProperty("accountCount").GetInt32());
        Assert.False(staff.GetProperty("isSystem").GetBoolean());
        Assert.Equal("Active", staff.GetProperty("status").GetString());
        Assert.True(roles.Single(r => r.GetProperty("code").GetString() == "ADMIN").GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task GetUserAccounts_ReturnsPagedEnvelope()
    {
        Authenticate(_admin);
        using var response = await _client.GetAsync("/user-accounts");
        var body = await CheckAsync(response, HttpStatusCode.OK);
        var data = body.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["ADMIN1", "OUTSIDE", "STAFF1"], items.Select(i => i.GetProperty("employeeCode").GetString()));
        var admin = items[0];
        Assert.Equal("Administrator", admin.GetProperty("roleName").GetString());
        Assert.Equal("ACTIVE", admin.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, admin.GetProperty("lastLoginAt").ValueKind);
        Assert.False(admin.TryGetProperty("EmployeeCode", out _));
    }

    [Fact]
    public async Task GetUserAccounts_FiltersByKeywordRoleDepartmentAndStatus()
    {
        Authenticate(_admin);
        Assert.Equal(["STAFF1"], await CodesAsync("/user-accounts?keyword=staff1"));
        Assert.Equal(["STAFF1"], await CodesAsync($"/user-accounts?roleId={_staffRole.Id}"));
        Assert.Equal(["STAFF1"], await CodesAsync($"/user-accounts?departmentId={_otherDepartment.Id}"));
        Assert.Equal(["STAFF1"], await CodesAsync("/user-accounts?status=LOCKED"));
        Assert.Equal(["ADMIN1", "OUTSIDE"], await CodesAsync("/user-accounts?status=ACTIVE"));
        Assert.Empty(await CodesAsync("/user-accounts?keyword=zzz-khong-co"));
    }

    [Fact]
    public async Task GetUserAccounts_PagesAndClampsPageSize()
    {
        Authenticate(_admin);
        using var second = await _client.GetAsync("/user-accounts?page=2&pageSize=2");
        var data = (await CheckAsync(second, HttpStatusCode.OK)).GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(["STAFF1"], data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("employeeCode").GetString()));

        using var beyond = await _client.GetAsync("/user-accounts?page=999999");
        Assert.Empty((await CheckAsync(beyond, HttpStatusCode.OK)).GetProperty("data").GetProperty("items").EnumerateArray());

        using var clamped = await _client.GetAsync("/user-accounts?pageSize=1000");
        Assert.Equal(100, (await CheckAsync(clamped, HttpStatusCode.OK)).GetProperty("data").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetUserAccounts_WithUnknownStatus_ReturnsEmptyList()
    {
        Authenticate(_admin);
        Assert.Empty(await CodesAsync("/user-accounts?status=BOGUS"));
    }

    [Fact]
    public async Task GetUserAccounts_WithMalformedGuid_Returns400ValidationError()
    {
        Authenticate(_admin);
        using var response = await _client.GetAsync("/user-accounts?roleId=not-a-guid");
        await CheckAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    private async Task<List<string?>> CodesAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        var body = await CheckAsync(response, HttpStatusCode.OK);
        return body.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("employeeCode").GetString()).ToList();
    }

    private void Authenticate(Account account)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "AdminAccountsHttpTests",
            ["Jwt:Audience"] = "AdminAccountsHttpTests",
        }).Build();
        var token = new JwtTokenProvider(configuration).CreateAccessToken(
            account.Id, account.Employee.Email, account.Employee.FullName, [], [], TimeSpan.FromMinutes(10));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    private void AppendLog(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_apiLog)
        {
            _apiLog.AppendLine(line);
        }
    }

    private static async Task<JsonElement> CheckAsync(HttpResponseMessage response, HttpStatusCode expected, string? errorCode = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(errorCode is null, root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("traceId").GetString()));
        if (errorCode is not null)
        {
            Assert.Equal(errorCode, root.GetProperty("errorCode").GetString());
            Assert.Equal((int)expected, root.GetProperty("status").GetInt32());
        }

        return root.Clone();
    }

    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var department = new Department { Id = Guid.NewGuid(), Code = "D", Name = "Department", LevelType = DepartmentLevelType.Department };
        _otherDepartment = new Department { Id = Guid.NewGuid(), Code = "D2", Name = "Other", LevelType = DepartmentLevelType.Department };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "J", Title = "Job" };
        db.Departments.AddRange(department, _otherDepartment);
        db.JobPositions.Add(job);
        _admin = Person("ADMIN1", department.Id, job.Id, now);
        _outsider = Person("OUTSIDE", department.Id, job.Id, now);
        var staff = Person("STAFF1", _otherDepartment.Id, job.Id, now);
        staff.IsLocked = true;
        db.Accounts.AddRange(_admin, _outsider, staff);
        var adminRole = new Role { Id = Guid.NewGuid(), Code = "ADMIN", Name = "Administrator", Status = RecordStatus.Active };
        _staffRole = new Role { Id = Guid.NewGuid(), Code = "STAFF", Name = "Staff", Status = RecordStatus.Active };
        var permission = new PermissionRecord { Id = Guid.NewGuid(), Code = "ROLE_UPDATE", Name = "Update roles", Resource = "role", Action = "update" };
        db.Roles.AddRange(adminRole, _staffRole);
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermissionAssignment { Id = Guid.NewGuid(), RoleId = adminRole.Id, PermissionId = permission.Id, Scope = "System" });
        db.UserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), AccountId = _admin.Id, RoleId = adminRole.Id },
            new UserRole { Id = Guid.NewGuid(), AccountId = staff.Id, RoleId = _staffRole.Id });
        await db.SaveChangesAsync();
    }

    private static Account Person(string code, Guid departmentId, Guid jobId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Username = code,
        PasswordHash = "unused-http-test-hash",
        LastSyncAt = now,
        UpdatedAt = now,
        Employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code}@example.test",
            DepartmentId = departmentId,
            JobPositionId = jobId,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        },
    };
}
