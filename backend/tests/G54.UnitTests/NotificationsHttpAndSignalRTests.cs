using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using G54.BLL.Dtos.Notification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// End-to-end HTTP + SignalR + PostgreSQL + Redis contract tests for GB-914 Notification System.
/// </summary>
public sealed class NotificationsHttpAndSignalRTests : IAsyncLifetime, IAsyncDisposable
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private Uri _address = null!;
    private System.Diagnostics.Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _admin = null!;
    private Account _manager = null!;
    private Account _staff = null!;
    private Employee _staffEmployee = null!;
    private Work _staffWork = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_notif_http_{Guid.NewGuid():N}";
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
        _address = new Uri($"http://127.0.0.1:{port}");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "G54.Api.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = _address.ToString();
        start.Environment["ConnectionStrings__Postgres"] = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        start.Environment["ConnectionStrings__Redis"] = redis;
        start.Environment["Jwt__Key"] = _jwtKey;
        start.Environment["Jwt__Issuer"] = "NotificationsHttpTests";
        start.Environment["Jwt__Audience"] = "NotificationsHttpTests";

        _api = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start the test API.");
        _api.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _api.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        _api.BeginOutputReadLine();
        _api.BeginErrorReadLine();
        _client = new HttpClient { BaseAddress = _address };

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_api.HasExited)
            {
                throw new InvalidOperationException($"G54.Api exited early:\n{_apiLog}");
            }

            try
            {
                using var health = await _client.GetAsync("/health");
                if (health.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                await Task.Delay(150);
            }
        }

        throw new TimeoutException($"G54.Api did not become healthy:\n{_apiLog}");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_api is { HasExited: false })
        {
            _api.Kill(entireProcessTree: true);
            await _api.WaitForExitAsync();
        }

        _api?.Dispose();
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();
        if (TestPostgres.AdminConnectionString is { } admin && !string.IsNullOrEmpty(_database))
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [PostgresFact]
    public async Task EndToEnd_Announcement_SignalRPush_Filters_MarkRead_And_Permissions()
    {
        // 1. Unauthenticated -> 401
        using (var anon = await _client.GetAsync("/notifications"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        }

        // 2. Staff (EMPLOYEE) cannot send announcements -> 403
        var staffToken = IssueToken(_staff, "EMPLOYEE", ["NOTIFICATION_READ"]);
        using (var forbidden = await SendWithTokenAsync(HttpMethod.Post, "/notifications", staffToken, new { title = "Test" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // 3. Connect SignalR hub as Staff & verify live push when Manager sends an IMPORTANT announcement
        var receivedTcs = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl($"{_address}hubs/notifications", options => options.AccessTokenProvider = () => Task.FromResult<string?>(staffToken))
            .Build();
        hub.On<NotificationDto>("notification", dto => receivedTcs.TrySetResult(dto));
        await hub.StartAsync();

        var managerToken = IssueToken(_manager, "DEPT_MANAGER", ["NOTIFICATION_READ", "NOTIFICATION_SEND"]);

        // 4. Manager fetches sendable recipients
        using (var recRes = await SendWithTokenAsync(HttpMethod.Get, "/notifications/recipients", managerToken))
        {
            Assert.Equal(HttpStatusCode.OK, recRes.StatusCode);
            using var recDoc = JsonDocument.Parse(await recRes.Content.ReadAsStringAsync());
            var recItems = recDoc.RootElement.GetProperty("data");
            Assert.Equal(1, recItems.GetArrayLength());
            Assert.Equal(_staffEmployee.Id, recItems[0].GetProperty("id").GetGuid());
        }

        // 5. Manager sends an IMPORTANT announcement linked to Staff's work
        using (var sendRes = await SendWithTokenAsync(
            HttpMethod.Post,
            "/notifications",
            managerToken,
            new
            {
                title = "Họp khẩn triển khai thẻ Q4",
                body = "Đề nghị kiểm tra đối soát mã giao dịch thẻ ngay trong chiều nay.",
                severity = "IMPORTANT",
                recipientEmployeeIds = new[] { _staffEmployee.Id },
                relatedType = "WORK",
                relatedId = _staffWork.Id,
            }))
        {
            Assert.Equal(HttpStatusCode.Created, sendRes.StatusCode);
            using var sendDoc = JsonDocument.Parse(await sendRes.Content.ReadAsStringAsync());
            Assert.Equal(1, sendDoc.RootElement.GetProperty("data").GetProperty("recipientCount").GetInt32());
        }

        // 6. Real SignalR WebSocket push arrives immediately
        var pushed = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("ANNOUNCEMENT", pushed.Type);
        Assert.Equal("IMPORTANT", pushed.Severity);
        Assert.Equal("Họp khẩn triển khai thẻ Q4", pushed.Title);
        Assert.Equal("WORK", pushed.RelatedType);
        Assert.Equal(_staffWork.Id, pushed.RelatedId);
        Assert.False(pushed.IsRead);

        // 7. Staff checks unread-count & filtered inbox (keyword + type + severity)
        using (var countRes = await SendWithTokenAsync(HttpMethod.Get, "/notifications/unread-count", staffToken))
        {
            Assert.Equal(HttpStatusCode.OK, countRes.StatusCode);
            using var countDoc = JsonDocument.Parse(await countRes.Content.ReadAsStringAsync());
            Assert.Equal(1, countDoc.RootElement.GetProperty("data").GetProperty("count").GetInt32());
        }

        using (var listRes = await SendWithTokenAsync(
            HttpMethod.Get,
            "/notifications?unread=true&type=ANNOUNCEMENT&severity=IMPORTANT&keyword=đối%20soát",
            staffToken))
        {
            Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
            using var listDoc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync());
            var items = listDoc.RootElement.GetProperty("data").GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(pushed.Id, items[0].GetProperty("id").GetGuid());
        }

        // 8. Manager cannot mark Staff's notification as read (IDOR check -> 404)
        using (var idorRes = await SendWithTokenAsync(HttpMethod.Post, $"/notifications/{pushed.Id}/read", managerToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, idorRes.StatusCode);
        }

        // 9. Staff marks notification read, unread count drops to 0
        using (var readRes = await SendWithTokenAsync(HttpMethod.Post, $"/notifications/{pushed.Id}/read", staffToken))
        {
            Assert.Equal(HttpStatusCode.OK, readRes.StatusCode);
            using var readDoc = JsonDocument.Parse(await readRes.Content.ReadAsStringAsync());
            Assert.True(readDoc.RootElement.GetProperty("data").GetProperty("isRead").GetBoolean());
        }

        using (var zeroRes = await SendWithTokenAsync(HttpMethod.Get, "/notifications/unread-count", staffToken))
        {
            using var zeroDoc = JsonDocument.Parse(await zeroRes.Content.ReadAsStringAsync());
            Assert.Equal(0, zeroDoc.RootElement.GetProperty("data").GetProperty("count").GetInt32());
        }
    }

    [PostgresFact]
    public async Task BruteForceLockout_RaisesSystemAlertInTheAdminInbox()
    {
        // The staff account gets a real hash so a wrong password is rejected (not an invalid-hash error).
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staff = await db.Accounts.SingleAsync(a => a.Id == _staff.Id);
            staff.PasswordHash = new PasswordEncoder().Encode("Correct#Pass123");
            await db.SaveChangesAsync();
        }

        // Default lockout threshold is 5 failures; the login rate limit allows exactly 5 attempts per minute.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await _client.PostAsJsonAsync("/auth/login", new { email = _staffEmployee.Email, password = $"wrong-{attempt}", rememberMe = false });
            Assert.Equal(attempt < 5 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }

        var adminToken = IssueToken(_admin, "SYSTEM_ADMIN", ["NOTIFICATION_READ"]);
        using var list = await SendWithTokenAsync(HttpMethod.Get, "/notifications?type=SYSTEM_ALERT&unread=true", adminToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("IMPORTANT", items[0].GetProperty("severity").GetString());
        Assert.Contains(_staffEmployee.EmployeeCode, items[0].GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var branch = new Department { Id = Guid.NewGuid(), Code = "BR", Name = "Chi nhánh", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var dept = new Department { Id = Guid.NewGuid(), Code = "KD", Name = "Phòng KD", ParentDepartmentId = branch.Id, LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var pos = new JobPosition { Id = Guid.NewGuid(), Code = "SALES", Title = "Sales", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "SYSTEM_ADMIN")
            ?? new Role { Id = Guid.NewGuid(), Code = "SYSTEM_ADMIN", Name = "System Admin", Status = RecordStatus.Active };
        var mgrRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "DEPT_MANAGER")
            ?? new Role { Id = Guid.NewGuid(), Code = "DEPT_MANAGER", Name = "Dept Manager", Status = RecordStatus.Active };
        var empRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "EMPLOYEE")
            ?? new Role { Id = Guid.NewGuid(), Code = "EMPLOYEE", Name = "Employee", Status = RecordStatus.Active };
        if (db.Entry(adminRole).State == EntityState.Detached) db.Roles.Add(adminRole);
        if (db.Entry(mgrRole).State == EntityState.Detached) db.Roles.Add(mgrRole);
        if (db.Entry(empRole).State == EntityState.Detached) db.Roles.Add(empRole);
        db.AddRange(branch, dept, pos);
        await db.SaveChangesAsync();

        var readPerm = await db.Permissions.SingleAsync(p => p.Code == "NOTIFICATION_READ");
        var sendPerm = await db.Permissions.SingleAsync(p => p.Code == "NOTIFICATION_SEND");
        await GrantIfMissingAsync(db, adminRole.Id, readPerm.Id, "All");
        await GrantIfMissingAsync(db, adminRole.Id, sendPerm.Id, "All");
        await GrantIfMissingAsync(db, mgrRole.Id, readPerm.Id, "Own");
        await GrantIfMissingAsync(db, mgrRole.Id, sendPerm.Id, "Dept");
        await GrantIfMissingAsync(db, empRole.Id, readPerm.Id, "Own");

        var adminEmp = new Employee { Id = Guid.NewGuid(), EmployeeCode = "ADM", FullName = "Admin", Email = "adm@bpms.local", DepartmentId = branch.Id, JobPositionId = pos.Id, Status = EmployeeStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var mgrEmp = new Employee { Id = Guid.NewGuid(), EmployeeCode = "MGR", FullName = "Trưởng phòng KD", Email = "mgr@bpms.local", DepartmentId = dept.Id, JobPositionId = pos.Id, Status = EmployeeStatus.Active, LastSyncAt = now, UpdatedAt = now };
        _staffEmployee = new Employee { Id = Guid.NewGuid(), EmployeeCode = "EMP", FullName = "Chuyên viên KD", Email = "emp@bpms.local", DepartmentId = dept.Id, JobPositionId = pos.Id, ManagerId = mgrEmp.Id, Status = EmployeeStatus.Active, LastSyncAt = now, UpdatedAt = now };
        _admin = new Account { Id = Guid.NewGuid(), EmployeeId = adminEmp.Id, Employee = adminEmp, Username = adminEmp.Email, PasswordHash = "hash", LastSyncAt = now, UpdatedAt = now };
        _manager = new Account { Id = Guid.NewGuid(), EmployeeId = mgrEmp.Id, Employee = mgrEmp, Username = mgrEmp.Email, PasswordHash = "hash", LastSyncAt = now, UpdatedAt = now };
        _staff = new Account { Id = Guid.NewGuid(), EmployeeId = _staffEmployee.Id, Employee = _staffEmployee, Username = _staffEmployee.Email, PasswordHash = "hash", LastSyncAt = now, UpdatedAt = now };

        _staffWork = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WRK-TEST-01",
            Title = "Mở thẻ tín dụng KH VIP",
            AssigneeEmployeeId = _staffEmployee.Id,
            CreatedBy = mgrEmp.Id,
            Priority = WorkPriority.High,
            Status = WorkStatus.InProgress,
            UpdatedAt = now,
        };

        db.AddRange(adminEmp, mgrEmp, _staffEmployee, _admin, _manager, _staff, _staffWork);
        db.UserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), AccountId = _admin.Id, RoleId = adminRole.Id },
            new UserRole { Id = Guid.NewGuid(), AccountId = _manager.Id, RoleId = mgrRole.Id },
            new UserRole { Id = Guid.NewGuid(), AccountId = _staff.Id, RoleId = empRole.Id });
        await db.SaveChangesAsync();
    }

    private static async Task GrantIfMissingAsync(AppDbContext db, Guid roleId, Guid permissionId, string scope)
    {
        if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId))
        {
            db.RolePermissions.Add(new RolePermissionAssignment { Id = Guid.NewGuid(), RoleId = roleId, PermissionId = permissionId, Scope = scope });
        }
    }

    private string IssueToken(Account account, string roleCode, IReadOnlyList<string> permissions)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = _jwtKey,
                ["Jwt:Issuer"] = "NotificationsHttpTests",
                ["Jwt:Audience"] = "NotificationsHttpTests",
            })
            .Build();
        return new JwtTokenProvider(config)
            .CreateAccessToken(account.Id, account.Employee.Email, account.Employee.FullName, [roleCode], permissions, TimeSpan.FromMinutes(15))
            .Value;
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(req);
    }

    private void AppendLog(string? line)
    {
        if (line is not null)
        {
            lock (_apiLog)
            {
                _apiLog.AppendLine(line);
            }
        }
    }
}
