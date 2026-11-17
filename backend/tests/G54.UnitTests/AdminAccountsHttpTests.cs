using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _admin = null!;
    private Account _outsider = null!;
    private Role _staffRole = null!;
    private Department _otherDepartment = null!;
    private Employee _eligibleEmployee = null!;
    private Employee _secondEligibleEmployee = null!;

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
        Assert.Equal("Department", admin.GetProperty("departmentName").GetString());
        Assert.Equal("Administrator", admin.GetProperty("roleName").GetString());
        Assert.Equal("ACTIVE", admin.GetProperty("status").GetString());
        Assert.False(admin.TryGetProperty("lastLoginAt", out _));
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

    [Fact]
    public async Task FullAccountLifecycle_SummaryEligibleCreateDetailUpdateResetLockUnlockAndForcedPasswordChange()
    {
        var capturePath = Environment.GetEnvironmentVariable("GB912_CAPTURE_PATH");
        var captures = new Dictionary<string, object>();

        async Task<(HttpStatusCode Status, JsonElement Root, string Raw)> ExchangeAsync(
            string key,
            HttpMethod method,
            string path,
            object? payload = null,
            string authNote = "Bearer token của tài khoản có quyền role:update (System)")
        {
            using var req = new HttpRequestMessage(method, path);
            string? reqJson = null;
            if (payload is not null)
            {
                reqJson = JsonSerializer.Serialize(payload, IndentedJson);
                req.Content = JsonContent.Create(payload);
            }

            using var resp = await _client.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var pretty = JsonSerializer.Serialize(doc.RootElement, IndentedJson);
            captures[key] = new
            {
                method = method.Method,
                path,
                auth = authNote,
                requestBody = reqJson,
                statusCode = (int)resp.StatusCode,
                responseBody = pretty,
            };
            return (resp.StatusCode, doc.RootElement.Clone(), pretty);
        }

        // 0. 401 & 403 checks on new endpoints
        _client.DefaultRequestHeaders.Authorization = null;
        var unauth = await ExchangeAsync("GB-912-12-401", HttpMethod.Get, "/user-accounts/summary", authNote: "Không gửi Authorization header");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.Status);

        Authenticate(_outsider);
        var forbid = await ExchangeAsync("GB-912-12-403", HttpMethod.Get, "/user-accounts/summary", authNote: "Bearer token của nhân viên thường (không có quyền role:update)");
        Assert.Equal(HttpStatusCode.Forbidden, forbid.Status);

        Authenticate(_admin);

        // 1. Summary counts
        var summary = await ExchangeAsync("GB-912-01", HttpMethod.Get, "/user-accounts/summary");
        Assert.Equal(HttpStatusCode.OK, summary.Status);
        Assert.Equal(2, summary.Root.GetProperty("data").GetProperty("activeCount").GetInt32());
        Assert.Equal(1, summary.Root.GetProperty("data").GetProperty("lockedCount").GetInt32());
        Assert.Equal(2, summary.Root.GetProperty("data").GetProperty("employeesWithoutAccountCount").GetInt32());

        // 2. List with departmentName and without lastLoginAt
        var list = await ExchangeAsync("GB-912-02", HttpMethod.Get, "/user-accounts");
        Assert.Equal(HttpStatusCode.OK, list.Status);
        var firstItem = list.Root.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("Department", firstItem.GetProperty("departmentName").GetString());
        Assert.False(firstItem.TryGetProperty("lastLoginAt", out _));

        // 3. Eligible employees
        var eligible = await ExchangeAsync("GB-912-03", HttpMethod.Get, "/user-accounts/eligible-employees");
        Assert.Equal(HttpStatusCode.OK, eligible.Status);
        var eligibleItems = eligible.Root.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, eligibleItems.Count);
        Assert.Equal("ELIG1", eligibleItems[0].GetProperty("employeeCode").GetString());

        // 4. Create account (201 Created + temporaryPassword + must_change_password)
        var createReq = new
        {
            employeeId = _eligibleEmployee.Id,
            username = "elig1.teller",
            roleId = _staffRole.Id,
            mustChangePassword = true,
        };
        var created = await ExchangeAsync("GB-912-04", HttpMethod.Post, "/user-accounts", createReq);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var createdAccount = created.Root.GetProperty("data").GetProperty("account");
        var createdId = createdAccount.GetProperty("id").GetGuid();
        var initialTempPwd = created.Root.GetProperty("data").GetProperty("temporaryPassword").GetString()!;
        Assert.Equal(16, initialTempPwd.Length);

        // 5. Conflict cases: ACCOUNT_EXISTS and USERNAME_EXISTS
        var dupAccount = await ExchangeAsync("GB-912-05-ACC", HttpMethod.Post, "/user-accounts", new
        {
            employeeId = _eligibleEmployee.Id,
            username = "another.username",
            roleId = _staffRole.Id,
            mustChangePassword = true,
        });
        Assert.Equal(HttpStatusCode.Conflict, dupAccount.Status);
        Assert.Equal("ACCOUNT_EXISTS", dupAccount.Root.GetProperty("errorCode").GetString());

        var dupUsername = await ExchangeAsync("GB-912-05-USR", HttpMethod.Post, "/user-accounts", new
        {
            employeeId = _secondEligibleEmployee.Id,
            username = "elig1.teller",
            roleId = _staffRole.Id,
            mustChangePassword = true,
        });
        Assert.Equal(HttpStatusCode.Conflict, dupUsername.Status);
        Assert.Equal("USERNAME_EXISTS", dupUsername.Root.GetProperty("errorCode").GetString());

        // 6. Get account detail (200 + 404)
        var detail = await ExchangeAsync("GB-912-06-OK", HttpMethod.Get, $"/user-accounts/{createdId}");
        Assert.Equal(HttpStatusCode.OK, detail.Status);
        Assert.Equal("elig1.teller", detail.Root.GetProperty("data").GetProperty("username").GetString());
        Assert.Equal("Other", detail.Root.GetProperty("data").GetProperty("departmentName").GetString());

        var detail404 = await ExchangeAsync("GB-912-06-404", HttpMethod.Get, $"/user-accounts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, detail404.Status);
        Assert.Equal("NOT_FOUND", detail404.Root.GetProperty("errorCode").GetString());

        // 7. Update account role (PUT /user-accounts/{id})
        var rolesResp = await _client.GetAsync("/roles");
        var adminRoleId = (await CheckAsync(rolesResp, HttpStatusCode.OK))
            .GetProperty("data").EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "ADMIN")
            .GetProperty("id").GetGuid();

        var updated = await ExchangeAsync("GB-912-07", HttpMethod.Put, $"/user-accounts/{createdId}", new { roleId = adminRoleId });
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("Administrator", updated.Root.GetProperty("data").GetProperty("roleName").GetString());

        // 8. Reset password (POST /user-accounts/{id}/reset-password)
        var reset = await ExchangeAsync("GB-912-08", HttpMethod.Post, $"/user-accounts/{createdId}/reset-password");
        Assert.Equal(HttpStatusCode.OK, reset.Status);
        var resetTempPwd = reset.Root.GetProperty("data").GetProperty("temporaryPassword").GetString()!;
        Assert.NotEqual(initialTempPwd, resetTempPwd);

        // 9. Lock & Unlock account (POST /user-accounts/{id}/lock & /unlock)
        var locked = await ExchangeAsync("GB-912-09-LOCK", HttpMethod.Post, $"/user-accounts/{createdId}/lock");
        Assert.Equal(HttpStatusCode.OK, locked.Status);
        Assert.Equal("LOCKED", locked.Root.GetProperty("data").GetProperty("status").GetString());

        var unlocked = await ExchangeAsync("GB-912-09-UNLOCK", HttpMethod.Post, $"/user-accounts/{createdId}/unlock");
        Assert.Equal(HttpStatusCode.OK, unlocked.Status);
        Assert.Equal("ACTIVE", unlocked.Root.GetProperty("data").GetProperty("status").GetString());

        // 10. Cannot lock self (400 CANNOT_LOCK_SELF)
        var selfLock = await ExchangeAsync("GB-912-10", HttpMethod.Post, $"/user-accounts/{_admin.Id}/lock");
        Assert.Equal(HttpStatusCode.BadRequest, selfLock.Status);
        Assert.Equal("CANNOT_LOCK_SELF", selfLock.Root.GetProperty("errorCode").GetString());

        // 11. Login with temporary password -> user.mustChangePassword = true -> POST /me/password -> user.mustChangePassword = false
        _client.DefaultRequestHeaders.Authorization = null;
        var login = await ExchangeAsync("GB-912-11-LOGIN", HttpMethod.Post, "/auth/login", new
        {
            email = _eligibleEmployee.Email,
            password = resetTempPwd,
            rememberMe = false,
        }, authNote: "Public endpoint (/auth/login)");
        Assert.Equal(HttpStatusCode.OK, login.Status);
        Assert.True(login.Root.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
        var userAccess = login.Root.GetProperty("accessToken").GetString()!;
        var userRefresh = login.Root.GetProperty("refreshToken").GetString()!;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAccess);
        var changedPwd = await ExchangeAsync("GB-912-11-CHANGE", HttpMethod.Post, "/me/password", new
        {
            currentPassword = resetTempPwd,
            newPassword = "NewSecurePassword2026!",
            refreshToken = userRefresh,
        }, authNote: "Bearer token của tài khoản vừa đăng nhập bằng mật khẩu tạm");
        Assert.Equal(HttpStatusCode.OK, changedPwd.Status);
        Assert.False(changedPwd.Root.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());

        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            var directory = Path.GetDirectoryName(capturePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                capturePath,
                JsonSerializer.Serialize(captures, IndentedJson));
        }
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
        _eligibleEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "ELIG1",
            FullName = "Phạm Minh Khoa",
            Email = "khoa.pham@example.test",
            DepartmentId = _otherDepartment.Id,
            JobPositionId = job.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        _secondEligibleEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "ELIG2",
            FullName = "Lê Thu Trang",
            Email = "trang.le@example.test",
            DepartmentId = _otherDepartment.Id,
            JobPositionId = job.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Employees.AddRange(_eligibleEmployee, _secondEligibleEmployee);
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
