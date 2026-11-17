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

/// <summary>
/// End-to-end HTTP + PostgreSQL + Redis contract tests for System Parameters &amp; Ranking Policy Studio (GB-910).
/// </summary>
public sealed class SystemParametersHttpTests : IAsyncLifetime, IAsyncDisposable
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private System.Diagnostics.Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _admin = null!;
    private Account _staff = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_sysparam_http_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDataAccess(new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString);
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
        start.Environment["Jwt__Issuer"] = "SystemParametersHttpTests";
        start.Environment["Jwt__Audience"] = "SystemParametersHttpTests";

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
                throw new InvalidOperationException($"Test API exited with {_api.ExitCode}: {_apiLog}");
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
                // Wait for Kestrel.
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

    [Fact]
    public async Task GB910_15_AuthAndPermissions_EnforcedAcrossSystemParameterAndRankingPolicyEndpoints()
    {
        using var unauth1 = await _client.GetAsync("/system-parameters");
        await CheckAsync(unauth1, HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        using var unauth2 = await _client.GetAsync("/system-parameters/ranking-policy");
        await CheckAsync(unauth2, HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        Authenticate(_staff);
        using var forbidden1 = await _client.GetAsync("/system-parameters");
        await CheckAsync(forbidden1, HttpStatusCode.Forbidden, "FORBIDDEN");

        using var forbidden2 = await _client.PutAsJsonAsync("/system-parameters", new { changes = new[] { new { key = "importMaxFileMb", value = 8 } } });
        await CheckAsync(forbidden2, HttpStatusCode.Forbidden, "FORBIDDEN");

        using var forbidden3 = await _client.GetAsync("/system-parameters/ranking-policy");
        await CheckAsync(forbidden3, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task GB910_16_SystemParameters_TypedUpdatePersistsAuditsAndValidatesOverHttp()
    {
        Authenticate(_admin);

        using var getRes = await _client.GetAsync("/system-parameters");
        var getBody = await CheckAsync(getRes, HttpStatusCode.OK);
        var initialItems = getBody.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(SystemParameterCatalog.All.Count, initialItems.Count);

        var updatePayload = new
        {
            changes = new object[]
            {
                new { key = "kpiLockTime", value = "22:15:00" },
                new { key = "perfAttentionDropPoints", value = 2.25 },
                new { key = "tierAQuotaPercent", value = 25 },
                new { key = "workOverdueEnabled", value = false },
            },
        };
        using var putRes = await _client.PutAsJsonAsync("/system-parameters", updatePayload);
        var putBody = await CheckAsync(putRes, HttpStatusCode.OK);
        var updatedItems = putBody.GetProperty("data").EnumerateArray().ToDictionary(x => x.GetProperty("key").GetString()!);

        Assert.Equal("22:15:00", updatedItems["kpiLockTime"].GetProperty("value").GetString());
        Assert.Equal(2.25m, updatedItems["perfAttentionDropPoints"].GetProperty("value").GetDecimal());
        Assert.Equal(25m, updatedItems["tierAQuotaPercent"].GetProperty("value").GetDecimal());
        Assert.False(updatedItems["workOverdueEnabled"].GetProperty("value").GetBoolean());
        Assert.Equal("SYSADMIN", updatedItems["kpiLockTime"].GetProperty("updatedBy").GetString());

        using var badRes = await _client.PutAsJsonAsync("/system-parameters", new
        {
            changes = new object[] { new { key = "kpiLockTime", value = "99:99:99" } },
        });
        await CheckAsync(badRes, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task GB910_17_RankingPolicyStudio_GetSimulateAndUpdateWorkEndToEndOverHttp()
    {
        Authenticate(_admin);

        using var getPolicyRes = await _client.GetAsync("/system-parameters/ranking-policy");
        var policyBody = await CheckAsync(getPolicyRes, HttpStatusCode.OK);
        var policyData = policyBody.GetProperty("data");
        Assert.Equal("DEFAULT_POLICY", policyData.GetProperty("code").GetString());
        Assert.Equal(1, policyData.GetProperty("version").GetInt32());

        var customDefinition = new
        {
            scoreFormula = "IF(DepartmentAvgScore < 70, ROUND(TotalScore * 0.85, 2), TotalScore)",
            tiers = new[]
            {
                new { tierCode = "A1", rankOrder = 1, minScore = 94m },
                new { tierCode = "A2", rankOrder = 2, minScore = 89m },
                new { tierCode = "A3", rankOrder = 3, minScore = 84m },
                new { tierCode = "B", rankOrder = 4, minScore = 70m },
                new { tierCode = "C", rankOrder = 5, minScore = 0m },
            },
            tieBreakers = new[] { "EFFECTIVE_SCORE_DESC", "SERVICE_QUALITY_SCORE_DESC", "BUSINESS_SCORE_DESC", "EMPLOYEE_CODE_ASC" },
            rules = new object[]
            {
                new
                {
                    id = "rule-risk-cap",
                    name = "Hạ trần hạng C cho rủi ro từ mức HIGH trở lên",
                    enabled = true,
                    stopOnMatch = false,
                    conditions = new
                    {
                        logicalOperator = "AND",
                        items = new[] { new { metric = "MAX_RISK_SEVERITY", @operator = "GTE", value = 3m } },
                        groups = Array.Empty<object>(),
                    },
                    actions = new[]
                    {
                        new { type = "CAP_MAX_TIER", tierCode = "C", message = "Khống chế trần hạng C khi có rủi ro HIGH/CRITICAL" },
                    },
                },
            },
        };

        using var simRes = await _client.PostAsJsonAsync("/system-parameters/ranking-policy/simulate", new
        {
            periodId = (Guid?)null,
            departmentId = (Guid?)null,
            definition = customDefinition,
        });
        var simBody = await CheckAsync(simRes, HttpStatusCode.OK);
        var simData = simBody.GetProperty("data");
        Assert.True(simData.GetProperty("totalEmployees").GetInt32() > 0);
        Assert.True(simData.GetProperty("changedCount").GetInt32() > 0);

        using var putPolicyRes = await _client.PutAsJsonAsync("/system-parameters/ranking-policy", new
        {
            name = "Chính sách Quý 4 tự động hóa",
            description = "Ưu tiên dịch vụ khách hàng và siết rủi ro HIGH",
            evaluationPeriodId = (Guid?)null,
            definition = customDefinition,
        });
        var savedBody = await CheckAsync(putPolicyRes, HttpStatusCode.OK);
        var savedData = savedBody.GetProperty("data");
        Assert.Equal(2, savedData.GetProperty("version").GetInt32());
        Assert.Equal("Chính sách Quý 4 tự động hóa", savedData.GetProperty("name").GetString());
    }

    private void Authenticate(Account account)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "SystemParametersHttpTests",
            ["Jwt:Audience"] = "SystemParametersHttpTests",
        }).Build();
        var token = new JwtTokenProvider(configuration).CreateAccessToken(
            account.Id, account.Employee.Email, account.Employee.FullName, [], [], TimeSpan.FromMinutes(10));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    private void AppendLog(string? line)
    {
        if (line is null) return;
        lock (_apiLog)
        {
            _apiLog.AppendLine(line);
        }
    }

    private async Task<JsonElement> CheckAsync(HttpResponseMessage response, HttpStatusCode expected, string? errorCode = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != expected)
        {
            // The API process appends to the log from another thread, so read it under the same lock.
            string log;
            lock (_apiLog)
            {
                log = _apiLog.ToString();
            }

            Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}: {text}\nAPI LOG:\n{log}");
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(errorCode is null, root.GetProperty("success").GetBoolean());
        if (errorCode is not null)
        {
            Assert.Equal(errorCode, root.GetProperty("errorCode").GetString());
        }

        return root.Clone();
    }

    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var department = new Department { Id = Guid.NewGuid(), Code = "D-ADM", Name = "Admin Dept", LevelType = DepartmentLevelType.Department };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "J-ADM", Title = "Admin" };
        db.Departments.Add(department);
        db.JobPositions.Add(job);

        _admin = Person("SYSADMIN", department.Id, job.Id, now);
        _staff = Person("STAFF01", department.Id, job.Id, now);
        db.Accounts.AddRange(_admin, _staff);

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "SYSTEM_ADMIN");
        if (adminRole is null)
        {
            adminRole = new Role { Id = Guid.NewGuid(), Code = "SYSTEM_ADMIN", Name = "System Admin", Status = RecordStatus.Active };
            db.Roles.Add(adminRole);
        }

        var viewPerm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == "SYSTEM_PARAMETER_VIEW")
            ?? new PermissionRecord { Id = Guid.NewGuid(), Code = "SYSTEM_PARAMETER_VIEW", Name = "View", Resource = "system_parameter", Action = "view" };
        if (db.Entry(viewPerm).State == EntityState.Detached) db.Permissions.Add(viewPerm);

        var updatePerm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == "SYSTEM_PARAMETER_UPDATE")
            ?? new PermissionRecord { Id = Guid.NewGuid(), Code = "SYSTEM_PARAMETER_UPDATE", Name = "Update", Resource = "system_parameter", Action = "update" };
        if (db.Entry(updatePerm).State == EntityState.Detached) db.Permissions.Add(updatePerm);

        await db.SaveChangesAsync();

        if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == viewPerm.Id))
        {
            db.RolePermissions.Add(new RolePermissionAssignment { Id = Guid.NewGuid(), RoleId = adminRole.Id, PermissionId = viewPerm.Id, Scope = "All" });
        }

        if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == updatePerm.Id))
        {
            db.RolePermissions.Add(new RolePermissionAssignment { Id = Guid.NewGuid(), RoleId = adminRole.Id, PermissionId = updatePerm.Id, Scope = "All" });
        }

        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = _admin.Id, RoleId = adminRole.Id });
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
