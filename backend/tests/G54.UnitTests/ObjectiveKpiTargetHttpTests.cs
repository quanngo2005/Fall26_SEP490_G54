using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
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
/// One real API process on a fresh PostgreSQL database shared by the UC35-UC44 tests (business objectives, KPI definitions,
/// target assignments). Set <c>G54_EVIDENCE_PATH</c> to also dump every recorded request/response as JSON.
/// </summary>
public sealed class ObjectiveKpiTargetFixture : IAsyncLifetime, IDisposable
{
    private const string Issuer = "ObjectiveKpiTargetHttpTests";
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Compact = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly List<Dictionary<string, object?>> _records = [];
    private readonly StringBuilder _apiLog = new();
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private System.Diagnostics.Process _api = null!;
    private HttpClient _client = null!;
    private readonly Dictionary<string, Account> _accounts = [];

    public void Dispose() => _client?.Dispose();

    public Guid BranchId { get; private set; }

    public Guid DeptAId { get; private set; }

    public Guid DeptBId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid InactiveMemberId { get; private set; }

    public Guid OtherBranchMemberId { get; private set; }

    public Guid GroupId { get; private set; }

    public Guid WorkTypeId { get; private set; }

    public Guid InactiveEmployeeId { get; private set; }

    public Guid PeriodId { get; private set; }

    public string Suffix { get; } = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_uc35_44_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(connectionString);
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddScoped(provider => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .EnableServiceProviderCaching(false).Options);
        _provider = services.BuildServiceProvider();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await SeedAsync(db);
        }

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
        start.Environment["ConnectionStrings__Postgres"] = connectionString;
        start.Environment["ConnectionStrings__Redis"] = redis;
        start.Environment["Jwt__Key"] = _jwtKey;
        start.Environment["Jwt__Issuer"] = Issuer;
        start.Environment["Jwt__Audience"] = Issuer;
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
        if (Environment.GetEnvironmentVariable("G54_EVIDENCE_PATH") is { Length: > 0 } path)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_records));
        }

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
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Sends a request as <paramref name="who"/> (none, admin, admin2, bm, outsider); records it when <paramref name="caseId"/> is set.</summary>
    public async Task<ApiResult> SendAsync(string? caseId, HttpMethod method, string path, string who, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (who != "none")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(who));
        }

        string? requestJson = null;
        if (body is not null)
        {
            requestJson = body as string ?? JsonSerializer.Serialize(body, Compact);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement root = default;
        var pretty = string.Empty;
        if (!string.IsNullOrWhiteSpace(text))
        {
            using var document = JsonDocument.Parse(text);
            root = document.RootElement.Clone();
            pretty = JsonSerializer.Serialize(root, Pretty);
        }

        if (caseId is not null)
        {
            string? prettyRequest = null;
            if (requestJson is not null)
            {
                using var requestDocument = JsonDocument.Parse(requestJson);
                prettyRequest = JsonSerializer.Serialize(requestDocument.RootElement, Pretty);
            }

            lock (_records)
            {
                _records.Add(new Dictionary<string, object?>
                {
                    ["case"] = caseId,
                    ["method"] = method.Method,
                    ["path"] = path,
                    ["who"] = who,
                    ["request"] = prettyRequest,
                    ["status"] = (int)response.StatusCode,
                    ["location"] = response.Headers.Location?.ToString(),
                    ["body"] = pretty,
                });
            }
        }

        return new ApiResult(response.StatusCode, root, response.Headers.Location);
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>The employee id of one of the seeded accounts (admin, admin2, bm, outsider, mgr, emp1, emp2, emp3).</summary>
    public Guid EmployeeIdOf(string who) => _accounts[who].EmployeeId;

    /// <summary>Stores a work assigned to <paramref name="assignee"/>, created by the manager account.</summary>
    public async Task<Guid> NewWorkAsync(string assignee, WorkStatus status = WorkStatus.New, DateOnly? plannedEnd = null, Guid? workType = null)
    {
        var id = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.Works.Add(new Work
            {
                Id = id,
                WorkTypeId = workType ?? WorkTypeId,
                AssigneeEmployeeId = _accounts[assignee].EmployeeId,
                CreatedBy = _accounts["mgr"].EmployeeId,
                Code = $"W-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
                Title = "Liên hệ khách hàng tiềm năng",
                Priority = WorkPriority.Medium,
                Status = status,
                PlannedStartDate = new DateOnly(2031, 1, 1),
                PlannedEndDate = plannedEnd ?? new DateOnly(2031, 12, 31),
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    public async Task<Guid> NewPendingHandoverAsync(Guid workId, string from, string to)
    {
        var id = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.TaskHandovers.Add(new TaskHandover
            {
                Id = id,
                WorkId = workId,
                FromEmployeeId = _accounts[from].EmployeeId,
                ToEmployeeId = _accounts[to].EmployeeId,
                Reason = "Khách hàng ở khu vực nhóm 2",
                Status = HandoverStatus.Pending,
                HandoverAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    public async Task<Guid> NewKpiAsync(KpiStatus status = KpiStatus.Active)
    {
        var id = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.KpiDefinitions.Add(new KpiDefinition
            {
                Id = id,
                KpiGroupId = GroupId,
                Code = $"K{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
                Name = "Seeded KPI",
                Unit = "VND",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    private string TokenFor(string who)
    {
        var account = _accounts[who];
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Issuer,
        }).Build();
        string[] roles = who switch { "admin" or "admin2" => ["ADMIN"], "bm" => ["BRANCH_MANAGER"], _ => [] };
        return new JwtTokenProvider(configuration).CreateAccessToken(
            account.Id, account.Employee.Email, account.Employee.FullName, roles, [], TimeSpan.FromMinutes(30)).Value;
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

    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var branch = Dept($"UB-{Suffix}", DepartmentLevelType.Branch, null, now);
        var deptA = Dept($"UA-{Suffix}", DepartmentLevelType.Department, branch.Id, now);
        var deptB = Dept($"UD-{Suffix}", DepartmentLevelType.Department, branch.Id, now);
        var otherBranch = Dept($"UO-{Suffix}", DepartmentLevelType.Branch, null, now);
        db.Departments.AddRange(branch, deptA, deptB, otherBranch);
        var job = new JobPosition { Id = Guid.NewGuid(), Code = $"UJ-{Suffix}", Title = "Job", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = $"UG-{Suffix}", Name = "Group", Status = KpiStatus.Active, UpdatedAt = now };
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"UP-{Suffix}",
            Name = "Year",
            PeriodType = PeriodType.Year,
            Status = PeriodStatus.Open,
            StartDate = new DateOnly(2031, 1, 1),
            EndDate = new DateOnly(2031, 12, 31),
            UpdatedAt = now,
        };
        db.AddRange(job, group, period);
        await db.SaveChangesAsync();

        var adminRole = await RoleAsync(db, "ADMIN", now);
        var bmRole = await RoleAsync(db, "BRANCH_MANAGER", now);
        foreach (var (who, code, deptId, role) in new[]
        {
            ("admin", "UADM1", branch.Id, adminRole), ("admin2", "UADM2", branch.Id, adminRole),
            ("bm", "UBM", branch.Id, bmRole), ("outsider", "UOUT", deptB.Id, (Role?)null),
        })
        {
            var account = Person($"{code}-{Suffix}", deptId, job.Id, now);
            db.Accounts.Add(account);
            if (role is not null)
            {
                db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id, UpdatedAt = now });
            }

            _accounts[who] = account;
        }

        await SeedWorkWorldAsync(db, deptA.Id, job.Id, now);
        var member = Person($"UMEM-{Suffix}", deptA.Id, job.Id, now);
        var inactive = Person($"UINA-{Suffix}", deptA.Id, job.Id, now);
        inactive.Employee.Status = EmployeeStatus.Terminated;
        var foreign = Person($"UFOR-{Suffix}", otherBranch.Id, job.Id, now);
        db.Accounts.AddRange(member, inactive, foreign);
        await db.SaveChangesAsync();
        (BranchId, DeptAId, DeptBId, GroupId, PeriodId) = (branch.Id, deptA.Id, deptB.Id, group.Id, period.Id);
        (MemberId, InactiveMemberId, OtherBranchMemberId) = (member.EmployeeId, inactive.EmployeeId, foreign.EmployeeId);
    }

    /// <summary>
    /// Work and handover world: manager mgr (direct manager of emp1) and employees emp1 (assignee), emp2, emp3 in one department,
    /// each with a role that carries the permissions the migrations define for it.
    /// </summary>
    private async Task SeedWorkWorldAsync(AppDbContext db, Guid departmentId, Guid jobId, DateTimeOffset now)
    {
        var workType = new WorkType { Id = Guid.NewGuid(), Code = $"WT-{Suffix}", Name = "Chăm sóc khách hàng", Status = RecordStatus.Active, UpdatedAt = now };
        db.WorkTypes.Add(workType);
        WorkTypeId = workType.Id;
        db.JobPositionWorkTypes.Add(new JobPositionWorkType { Id = Guid.NewGuid(), JobPositionId = jobId, WorkTypeId = workType.Id, UpdatedAt = now });

        var employeeRole = new Role { Id = Guid.NewGuid(), Code = $"WH_EMP_{Suffix}", Name = "WH employee", Status = RecordStatus.Active, UpdatedAt = now };
        var managerRole = new Role { Id = Guid.NewGuid(), Code = $"WH_MGR_{Suffix}", Name = "WH manager", Status = RecordStatus.Active, UpdatedAt = now };
        db.Roles.AddRange(employeeRole, managerRole);
        foreach (var (role, resource, action, scope) in new[]
        {
            (employeeRole, "work", "read", "Own"), (employeeRole, "work", "update", "Own"),
            (employeeRole, "handover", "create", "Own"), (employeeRole, "handover", "respond", "Own"),
            (managerRole, "work", "create", "Department"), (managerRole, "work", "read", "Department"), (managerRole, "work", "update", "Department"),
            (managerRole, "handover", "create", "Own"), (managerRole, "handover", "set_share", "Own"),
            (managerRole, "handover", "respond", "Own"), (managerRole, "handover", "manage", "Own"),
        })
        {
            var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Resource == resource && p.Action == action);
            if (permission is null)
            {
                permission = new PermissionRecord
                {
                    Id = Guid.NewGuid(),
                    Code = $"{resource}_{action}".ToUpperInvariant().Replace('-', '_'),
                    Name = $"{resource}:{action}",
                    Resource = resource,
                    Action = action,
                };
                db.Permissions.Add(permission);
                await db.SaveChangesAsync();
            }

            db.RolePermissions.Add(new RolePermissionAssignment { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permission.Id, Scope = scope });
        }

        await db.SaveChangesAsync();
        var manager = Person($"UMGR-{Suffix}", departmentId, jobId, now);
        db.Accounts.Add(manager);
        _accounts["mgr"] = manager;
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = manager.Id, RoleId = managerRole.Id, UpdatedAt = now });
        await db.SaveChangesAsync();
        foreach (var who in new[] { "emp1", "emp2", "emp3" })
        {
            var account = Person($"U{who.ToUpperInvariant()}-{Suffix}", departmentId, jobId, now);
            account.Employee.ManagerId = who == "emp1" ? manager.EmployeeId : null;
            db.Accounts.Add(account);
            _accounts[who] = account;
            db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = employeeRole.Id, UpdatedAt = now });
        }

        var inactive = Person($"UWHINA-{Suffix}", departmentId, jobId, now);
        inactive.Employee.Status = EmployeeStatus.Terminated;
        db.Accounts.Add(inactive);
        InactiveEmployeeId = inactive.EmployeeId;
        await db.SaveChangesAsync();
    }

    private static async Task<Role> RoleAsync(AppDbContext db, string code, DateTimeOffset now)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == code);
        if (role is null)
        {
            role = new Role { Id = Guid.NewGuid(), Code = code, Name = code, Status = RecordStatus.Active, UpdatedAt = now };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        return role;
    }

    private static Department Dept(string code, DepartmentLevelType level, Guid? parent, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        LevelType = level,
        ParentDepartmentId = parent,
        Status = RecordStatus.Active,
        LastSyncAt = now,
        UpdatedAt = now,
    };

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

public sealed record ApiResult(HttpStatusCode Status, JsonElement Root, Uri? Location)
{
    public int Code => (int)Status;

    public string? ErrorCode => Root.ValueKind == JsonValueKind.Object && Root.TryGetProperty("errorCode", out var e) ? e.GetString() : null;

    public JsonElement Data => Root.GetProperty("data");

    public Guid Id => Data.GetProperty("id").GetGuid();
}

[CollectionDefinition(Name)]
public sealed class ObjectiveKpiTargetDefinition : ICollectionFixture<ObjectiveKpiTargetFixture>
{
    public const string Name = "ObjectiveKpiTargetHttp";
}
