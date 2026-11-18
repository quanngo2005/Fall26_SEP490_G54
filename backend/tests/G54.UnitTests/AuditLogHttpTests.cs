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

/// <summary>Exercises the real API pipeline, JWT and permission service for the read-only audit log API.</summary>
public sealed class AuditLogHttpTests : IAsyncLifetime, IAsyncDisposable
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private System.Diagnostics.Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _viewer = null!;
    private Account _outsider = null!;
    private AuditLog _log = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_auditlog_http_{Guid.NewGuid():N}";
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
        start.Environment["Jwt__Issuer"] = "AuditLogHttpTests";
        start.Environment["Jwt__Audience"] = "AuditLogHttpTests";
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

    [Fact]
    public async Task GetAuditLogs_WithoutAuthentication_Returns401()
    {
        using var response = await _client.GetAsync("/audit-logs");
        await CheckAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task GetAuditLogs_WithoutPermission_Returns403()
    {
        Authenticate(_outsider);
        using var response = await _client.GetAsync("/audit-logs");
        await CheckAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task GetAuditLogs_ReturnsEnvelopeWithPagedDataInsideCamelCaseData()
    {
        Authenticate(_viewer);
        using var response = await _client.GetAsync("/audit-logs?page=1&pageSize=20");
        var body = await CheckAsync(response, HttpStatusCode.OK);
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(data.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        var first = items.EnumerateArray().First(i => i.GetProperty("id").GetGuid() == _log.Id);
        Assert.Equal(_log.EntityName, first.GetProperty("entityName").GetString());
        Assert.Equal(_log.EntityId, first.GetProperty("entityId").GetString());
        Assert.Equal(_log.Action, first.GetProperty("action").GetString());
        Assert.False(first.TryGetProperty("EntityName", out _));
    }

    [Fact]
    public async Task GetAuditLog_WithoutPermission_Returns403()
    {
        Authenticate(_outsider);
        using var response = await _client.GetAsync($"/audit-logs/{_log.Id}");
        await CheckAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task GetAuditLog_ReturnsDetailEnvelope()
    {
        Authenticate(_viewer);
        using var response = await _client.GetAsync($"/audit-logs/{_log.Id}");
        var body = await CheckAsync(response, HttpStatusCode.OK);
        var data = body.GetProperty("data");
        Assert.Equal(_log.Id, data.GetProperty("id").GetGuid());
        Assert.Equal(_log.EntityName, data.GetProperty("entityName").GetString());
        Assert.Equal(_log.HttpMethod, data.GetProperty("httpMethod").GetString());
        Assert.Equal(_log.RequestPath, data.GetProperty("requestPath").GetString());
    }

    [Fact]
    public async Task GetAuditLog_WhenIdDoesNotExist_Returns404NotFound()
    {
        Authenticate(_viewer);
        using var response = await _client.GetAsync($"/audit-logs/{Guid.NewGuid()}");
        await CheckAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task GetAuditLogs_WhenFromIsAfterTo_Returns400ValidationError()
    {
        Authenticate(_viewer);
        var from = DateTimeOffset.UtcNow;
        var to = from.AddDays(-1);
        using var response = await _client.GetAsync(
            $"/audit-logs?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        await CheckAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetAuditLogs_WhenPageSizeExceedsMaximum_Returns400ValidationError()
    {
        Authenticate(_viewer);
        using var response = await _client.GetAsync("/audit-logs?pageSize=101");
        await CheckAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task PostAuditLogs_Returns405MethodNotAllowed()
    {
        Authenticate(_viewer);
        using var response = await _client.PostAsJsonAsync("/audit-logs", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private void Authenticate(Account account, string[]? roles = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "AuditLogHttpTests",
            ["Jwt:Audience"] = "AuditLogHttpTests",
        }).Build();
        var token = new JwtTokenProvider(configuration).CreateAccessToken(
            account.Id, account.Employee.Email, account.Employee.FullName, roles ?? [], [], TimeSpan.FromMinutes(10));
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
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "J", Title = "Job" };
        db.Departments.Add(department);
        db.JobPositions.Add(job);
        _viewer = Person("VIEWER", department.Id, job.Id, now);
        _outsider = Person("OUTSIDE", department.Id, job.Id, now);
        db.Accounts.AddRange(_viewer, _outsider);
        var role = new Role { Id = Guid.NewGuid(), Code = "AUDITOR", Name = "Auditor", Status = RecordStatus.Active };
        var permission = new PermissionRecord { Id = Guid.NewGuid(), Code = "AUDIT_LOG_VIEW", Name = "View audit logs", Resource = "audit_log", Action = "view" };
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermissionAssignment { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permission.Id, Scope = "All" });
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = _viewer.Id, RoleId = role.Id });
        _log = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityName = "RiskEvent",
            EntityId = Guid.NewGuid().ToString(),
            Action = "Create",
            ActorId = _viewer.Id,
            ActorName = "VIEWER",
            IpAddress = "127.0.0.1",
            UserAgent = "curl/8.0",
            HttpMethod = "POST",
            RequestPath = "/risk-events",
            TraceId = "trace-seed",
            OccurredAt = now,
        };
        db.Set<AuditLog>().Add(_log);
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
