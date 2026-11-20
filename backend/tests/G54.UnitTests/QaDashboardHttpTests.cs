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

/// <summary>
/// SCR-PRF-04 QA dashboard over the real API pipeline: JWT, the permission grants the migrations give the QA and VERIFIER
/// roles, routing, query binding, the JSON envelope and error mapping. Contract: docs/api/QA_DASHBOARD_CONTRACT.md.
/// </summary>
public sealed class QaDashboardHttpTests : IAsyncLifetime, IAsyncDisposable
{
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private System.Diagnostics.Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _qa = null!;
    private Account _verifier = null!;
    private Account _outsider = null!;
    private EvaluationPeriod _period = null!;
    private Department _department = null!;
    private Department _otherDepartment = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_qadashboard_http_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(connectionString);
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
        start.Environment["ConnectionStrings__Postgres"] = connectionString;
        start.Environment["ConnectionStrings__Redis"] = redis;
        start.Environment["Jwt__Key"] = _jwtKey;
        start.Environment["Jwt__Issuer"] = "QaDashboardHttpTests";
        start.Environment["Jwt__Audience"] = "QaDashboardHttpTests";
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
    [InlineData("/qa-dashboard")]
    [InlineData("/qa-dashboard/filters")]
    [InlineData("/qa-dashboard/reconciliations")]
    public async Task Endpoints_WithoutAuthentication_Return401(string path)
    {
        using var response = await _client.GetAsync(path);
        await CheckAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData("/qa-dashboard")]
    [InlineData("/qa-dashboard/filters")]
    [InlineData("/qa-dashboard/reconciliations")]
    public async Task Endpoints_WithoutQualityFlagRead_Return403(string path)
    {
        Authenticate(_outsider);
        using var response = await _client.GetAsync(path);
        await CheckAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task GetDashboard_AsQa_ReturnsCamelCaseFiguresOfTheCurrentPeriod()
    {
        Authenticate(_qa, ["VERIFIER"]);
        using var response = await _client.GetAsync("/qa-dashboard");
        var data = (await CheckAsync(response, HttpStatusCode.OK)).GetProperty("data");

        Assert.Equal(_period.Id, data.GetProperty("period").GetProperty("id").GetGuid());
        Assert.False(data.GetProperty("period").GetProperty("isLocked").GetBoolean());
        Assert.Equal(1, data.GetProperty("banner").GetProperty("inReviewCount").GetInt32());

        var cards = data.GetProperty("kpiCards");
        Assert.Equal(3, cards.GetProperty("notMatched").GetProperty("count").GetInt32());
        Assert.Equal(1, cards.GetProperty("inReview").GetProperty("count").GetInt32());
        var risk = cards.GetProperty("riskEvents");
        Assert.Equal(2, risk.GetProperty("count").GetInt32());
        Assert.Equal(1, risk.GetProperty("openCount").GetInt32());
        Assert.Equal(13m, risk.GetProperty("totalPenaltyPoints").GetDecimal());

        var reasons = data.GetProperty("commonDiscrepancyReasons").EnumerateArray().ToList();
        Assert.Equal("EMPLOYEE_MISMATCH", reasons[0].GetProperty("reasonCode").GetString());
        Assert.Equal(2, reasons[0].GetProperty("count").GetInt32());
        Assert.Contains(reasons, r => r.GetProperty("reasonCode").GetString() == "NO_IMPORTED_TRANSACTION");

        var top = data.GetProperty("topDiscrepancyEmployees").EnumerateArray().ToList();
        Assert.Equal(["QA-A", "QA-B"], top.Select(e => e.GetProperty("employeeCode").GetString()));
        Assert.Equal(2, top[0].GetProperty("discrepancyCount").GetInt32());

        var weeks = data.GetProperty("matchedRateChart").GetProperty("weeks");
        Assert.Equal(8, weeks.GetArrayLength());
        Assert.False(data.TryGetProperty("KpiCards", out _));
    }

    [Fact]
    public async Task GetDashboard_AsVerifier_IsAllowedLikeQa()
    {
        Authenticate(_verifier, ["VERIFIER"]);
        using var response = await _client.GetAsync("/qa-dashboard");
        await CheckAsync(response, HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDashboard_FilteredByDepartment_OnlyCountsThatSubtree()
    {
        Authenticate(_qa, ["VERIFIER"]);
        using var response = await _client.GetAsync($"/qa-dashboard?periodId={_period.Id}&departmentId={_otherDepartment.Id}");
        var cards = (await CheckAsync(response, HttpStatusCode.OK)).GetProperty("data").GetProperty("kpiCards");

        Assert.Equal(1, cards.GetProperty("notMatched").GetProperty("count").GetInt32());
        Assert.Equal(0, cards.GetProperty("inReview").GetProperty("count").GetInt32());
        Assert.Equal(0, cards.GetProperty("riskEvents").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GetDashboard_UnknownPeriod_Returns404()
    {
        Authenticate(_qa, ["VERIFIER"]);
        using var response = await _client.GetAsync($"/qa-dashboard?periodId={Guid.NewGuid()}");
        await CheckAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task GetDashboard_MalformedPeriodId_Returns400()
    {
        Authenticate(_qa, ["VERIFIER"]);
        using var response = await _client.GetAsync("/qa-dashboard?periodId=not-a-guid");
        await CheckAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetFilters_ListsMonthlyPeriodsWithTheCurrentOneMarked()
    {
        Authenticate(_qa, ["VERIFIER"]);
        using var response = await _client.GetAsync("/qa-dashboard/filters");
        var data = (await CheckAsync(response, HttpStatusCode.OK)).GetProperty("data");

        var period = data.GetProperty("periods").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == _period.Id);
        Assert.True(period.GetProperty("isCurrent").GetBoolean());
        Assert.Contains(data.GetProperty("departments").EnumerateArray(), d => d.GetProperty("id").GetGuid() == _department.Id);
        Assert.True(data.GetProperty("jobPositions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetReconciliations_FiltersByStatusAndReason()
    {
        Authenticate(_qa, ["VERIFIER"]);
        using var response = await _client.GetAsync(
            $"/qa-dashboard/reconciliations?periodId={_period.Id}&status=NOT_MATCHED&reason=EMPLOYEE_MISMATCH&pageSize=1");
        var data = (await CheckAsync(response, HttpStatusCode.OK)).GetProperty("data");

        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("NOT_MATCHED", item.GetProperty("status").GetString());
        Assert.Equal("Sai mã nhân viên", item.GetProperty("reasonDescription").GetString());
    }

    [Fact]
    public async Task GetReconciliations_UnknownStatus_Returns400()
    {
        Authenticate(_qa, ["VERIFIER"]);
        using var response = await _client.GetAsync("/qa-dashboard/reconciliations?status=DONE");
        await CheckAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    private void Authenticate(Account account, string[]? roles = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "QaDashboardHttpTests",
            ["Jwt:Audience"] = "QaDashboardHttpTests",
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
        var today = DateOnly.FromDateTime(now.ToOffset(BusinessOffset).DateTime);
        _department = new Department { Id = Guid.NewGuid(), Code = "QAD-D1", Name = "QA Dept 1", LevelType = DepartmentLevelType.Department };
        _otherDepartment = new Department { Id = Guid.NewGuid(), Code = "QAD-D2", Name = "QA Dept 2", LevelType = DepartmentLevelType.Department };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "QAD-J", Title = "QA Job" };
        db.Departments.AddRange(_department, _otherDepartment);
        db.JobPositions.Add(job);

        _qa = Person("QA-USER", _department.Id, job.Id, now);
        _verifier = Person("QA-VERIFIER", _department.Id, job.Id, now);
        _outsider = Person("QA-OUTSIDE", _department.Id, job.Id, now);
        var a = Person("QA-A", _department.Id, job.Id, now);
        var b = Person("QA-B", _otherDepartment.Id, job.Id, now);
        db.Accounts.AddRange(_qa, _verifier, _outsider, a, b);

        // QA staff are verifiers: the migration ConsolidateRoles merged the QA role into VERIFIER, which holds quality_flag:read.
        var verifierRole = await db.Roles.SingleAsync(r => r.Code == "VERIFIER");
        db.UserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), AccountId = _qa.Id, RoleId = verifierRole.Id },
            new UserRole { Id = Guid.NewGuid(), AccountId = _verifier.Id, RoleId = verifierRole.Id });

        // The current calendar month, so the dashboard picks it without a periodId. The database only accepts it inside the
        // full hierarchy YEAR > HALF_YEAR > QUARTER > MONTH.
        var y = today.Year;
        var quarter = (today.Month - 1) / 3;
        var half = quarter / 2;
        var year = Period($"QAD-{y}", $"{y}", PeriodType.Year, new DateOnly(y, 1, 1), new DateOnly(y, 12, 31), null, now);
        var halfYear = Period(
            $"QAD-{y}-H{half + 1}", $"H{half + 1}/{y}", PeriodType.HalfYear,
            new DateOnly(y, half * 6 + 1, 1), new DateOnly(y, half * 6 + 6, DateTime.DaysInMonth(y, half * 6 + 6)), year.Id, now);
        var quarterPeriod = Period(
            $"QAD-{y}-Q{quarter + 1}", $"Q{quarter + 1}/{y}", PeriodType.Quarter,
            new DateOnly(y, quarter * 3 + 1, 1), new DateOnly(y, quarter * 3 + 3, DateTime.DaysInMonth(y, quarter * 3 + 3)), halfYear.Id, now);
        _period = Period(
            $"QAD-{today:yyyy-MM}", $"{today:MM/yyyy}", PeriodType.Month,
            new DateOnly(y, today.Month, 1), new DateOnly(y, today.Month, DateTime.DaysInMonth(y, today.Month)), quarterPeriod.Id, now);
        db.EvaluationPeriods.AddRange(year, halfYear, quarterPeriod, _period);
        await db.SaveChangesAsync();

        // Inside the period whatever day the test runs: the first day of the month, business time.
        var inPeriod = new DateTimeOffset(_period.StartDate.ToDateTime(new TimeOnly(9, 0)), BusinessOffset).ToUniversalTime();
        var workA = Work("QAD-W-A", a, now);
        var workB = Work("QAD-W-B", b, now);
        db.Works.AddRange(workA, workB);
        db.WorkTransactions.AddRange(
            Transaction(workA, "QAD-TX-1", WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", inPeriod),
            Transaction(workA, "QAD-TX-2", WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", inPeriod),
            Transaction(workB, "QAD-TX-3", WorkTransactionStatus.NotMatched, "PRODUCT_MISMATCH", inPeriod),
            Transaction(workA, "QAD-TX-4", WorkTransactionStatus.InReview, null, inPeriod),
            Transaction(workA, "QAD-TX-5", WorkTransactionStatus.Matched, null, inPeriod));

        db.RiskEvents.AddRange(
            Risk("QAD-R-1", a, RiskEventStatus.Open, 3m, inPeriod, _qa),
            Risk("QAD-R-2", a, RiskEventStatus.Resolved, 10m, inPeriod, _qa),
            Risk("QAD-R-3", a, RiskEventStatus.Dismissed, 50m, inPeriod, _qa));

        await db.SaveChangesAsync();
    }

    private static EvaluationPeriod Period(
        string code, string name, PeriodType type, DateOnly start, DateOnly end, Guid? parentId, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            StartDate = start,
            EndDate = end,
            PeriodType = type,
            Status = PeriodStatus.Open,
            ParentPeriodId = parentId,
            UpdatedAt = now,
        };

    private static Work Work(string code, Account assignee, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Title = code,
        AssigneeEmployeeId = assignee.Employee.Id,
        CreatedBy = assignee.Employee.Id,
        AssignedAt = now,
        Status = WorkStatus.Submitted,
        UpdatedAt = now,
    };

    private static WorkTransaction Transaction(Work work, string code, string status, string? reason, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        WorkId = work.Id,
        TransactionCode = code,
        Status = status,
        Reason = reason,
        CreatedAt = at,
        UpdatedAt = at,
    };

    private static RiskEvent Risk(string code, Account employee, RiskEventStatus status, decimal penalty, DateTimeOffset at, Account recorder) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        EmployeeId = employee.Employee.Id,
        RecordedBy = recorder.Employee.Id,
        EventType = "VIOLATION",
        Description = code,
        Severity = RiskEventSeverity.Low,
        Status = status,
        PenaltyPoints = penalty,
        OccurredAt = at,
        RecordedAt = at,
        UpdatedAt = at,
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
            Email = $"{code.ToLowerInvariant()}@example.test",
            DepartmentId = departmentId,
            JobPositionId = jobId,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        },
    };
}
