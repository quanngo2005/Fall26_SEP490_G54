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
/// Exercises the real API pipeline, JWT, the permissions seeded by the migrations (HR_OFFICER / RANKING_RECORD_READ) and
/// PostgreSQL for the HR ranking records API (GB-650).
/// </summary>
public sealed class RankingRecordHttpTests : IAsyncLifetime, IAsyncDisposable
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private System.Diagnostics.Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _hr = null!;
    private Account _outsider = null!;
    private EvaluationPeriod _locked = null!;
    private EvaluationPeriod _open = null!;
    private Department _sales = null!;
    private PerformanceEvaluation _top = null!;
    private PerformanceEvaluation _inReview = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_ranking_http_{Guid.NewGuid():N}";
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
        start.Environment["Jwt__Issuer"] = "RankingRecordHttpTests";
        start.Environment["Jwt__Audience"] = "RankingRecordHttpTests";
        _api = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start the test API.");
        _api.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _api.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        _api.BeginOutputReadLine();
        _api.BeginErrorReadLine();
        _client = new HttpClient { BaseAddress = address };
        for (var attempt = 0; attempt < 100; attempt++)
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
    public async Task WithoutAuthentication_Returns401()
    {
        using var response = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}");
        await CheckAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task WithoutThePermission_Returns403OnListAndDetail()
    {
        Authenticate(_outsider);

        using var list = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}");
        using var detail = await _client.GetAsync($"/ranking-records/{_top.Id}");

        await CheckAsync(list, HttpStatusCode.Forbidden, "FORBIDDEN");
        await CheckAsync(detail, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task List_ReturnsTheRankedRecordsInTheEnvelopeWithCamelCaseFields()
    {
        Authenticate(_hr);

        using var response = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}");

        var data = (await CheckAsync(response, HttpStatusCode.OK)).GetProperty("data");
        Assert.Equal((2, 1, 20), (data.GetProperty("totalCount").GetInt32(), data.GetProperty("page").GetInt32(), data.GetProperty("pageSize").GetInt32()));
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["NV-A", "NV-B"], items.Select(i => i.GetProperty("employeeCode").GetString()));
        var top = items[0];
        Assert.Equal(_top.Id, top.GetProperty("evaluationId").GetGuid());
        Assert.Equal((1, 2), (top.GetProperty("rank").GetInt32(), top.GetProperty("groupSize").GetInt32()));
        Assert.Equal(96m, top.GetProperty("totalScore").GetDecimal());
        Assert.Equal(("A1", "A1", false), (top.GetProperty("proposedTier").GetString(), top.GetProperty("tier").GetString(), top.GetProperty("isAdjusted").GetBoolean()));
        Assert.Equal(JsonValueKind.String, top.GetProperty("lockedAt").ValueKind);
        Assert.False(top.TryGetProperty("EmployeeCode", out _));
    }

    [Fact]
    public async Task List_AppliesFiltersAndPaging()
    {
        Authenticate(_hr);

        using var tier = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}&tier=a1");
        using var keyword = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}&keyword=nv-b&departmentId={_sales.Id}");
        using var paged = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}&page=2&pageSize=1");

        Assert.Equal(["NV-A"], Codes(await CheckAsync(tier, HttpStatusCode.OK)));
        Assert.Equal(["NV-B"], Codes(await CheckAsync(keyword, HttpStatusCode.OK)));
        var page2 = await CheckAsync(paged, HttpStatusCode.OK);
        Assert.Equal(["NV-B"], Codes(page2));
        Assert.Equal(2, page2.GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_RejectsInvalidInput()
    {
        Authenticate(_hr);

        using var noPeriod = await _client.GetAsync("/ranking-records");
        using var badTier = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}&tier=Z9");
        using var badPeriodId = await _client.GetAsync("/ranking-records?periodId=not-a-guid");
        using var unknownPeriod = await _client.GetAsync($"/ranking-records?periodId={Guid.NewGuid()}");
        using var unknownDepartment = await _client.GetAsync($"/ranking-records?periodId={_locked.Id}&departmentId={Guid.NewGuid()}");
        using var notLocked = await _client.GetAsync($"/ranking-records?periodId={_open.Id}");

        await CheckAsync(noPeriod, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await CheckAsync(badTier, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await CheckAsync(badPeriodId, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await CheckAsync(unknownPeriod, HttpStatusCode.NotFound, "NOT_FOUND");
        await CheckAsync(unknownDepartment, HttpStatusCode.NotFound, "NOT_FOUND");
        await CheckAsync(notLocked, HttpStatusCode.Conflict, "EVALUATION_PERIOD_INVALID_STATE");
    }

    [Fact]
    public async Task Detail_ReturnsTheRecordWithItsPeriodAndReviewInformation()
    {
        Authenticate(_hr);

        using var response = await _client.GetAsync($"/ranking-records/{_top.Id}");

        var data = (await CheckAsync(response, HttpStatusCode.OK)).GetProperty("data");
        Assert.Equal(_top.Id, data.GetProperty("record").GetProperty("evaluationId").GetGuid());
        Assert.Equal((_locked.Id, "Q3-LOCKED"), (data.GetProperty("periodId").GetGuid(), data.GetProperty("periodCode").GetString()));
        Assert.Equal("Phê duyệt", data.GetProperty("approvalComments").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("acknowledgedAt").ValueKind);
    }

    [Fact]
    public async Task Detail_Returns404ForAnUnknownIdOrANonFinalRecord()
    {
        Authenticate(_hr);

        using var unknown = await _client.GetAsync($"/ranking-records/{Guid.NewGuid()}");
        using var notFinal = await _client.GetAsync($"/ranking-records/{_inReview.Id}");
        using var notGuid = await _client.GetAsync("/ranking-records/abc");

        await CheckAsync(unknown, HttpStatusCode.NotFound, "NOT_FOUND");
        await CheckAsync(notFinal, HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal(HttpStatusCode.NotFound, notGuid.StatusCode);
    }

    [Fact]
    public async Task Writing_Returns405()
    {
        Authenticate(_hr);

        using var response = await _client.PostAsync("/ranking-records", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private static List<string?> Codes(JsonElement body) =>
        body.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("employeeCode").GetString()).ToList();

    private void Authenticate(Account account)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "RankingRecordHttpTests",
            ["Jwt:Audience"] = "RankingRecordHttpTests",
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
        var branch = new Department { Id = Guid.NewGuid(), Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch };
        _sales = new Department { Id = Guid.NewGuid(), Code = "SALES", Name = "Sales", LevelType = DepartmentLevelType.Department, ParentDepartmentId = branch.Id };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "J", Title = "Teller" };
        db.Departments.AddRange(branch, _sales);
        db.JobPositions.Add(job);
        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = job.Id,
            Code = "EP",
            Name = "Profile",
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = EvaluationProfileStatus.Draft,
            UpdatedAt = now,
        };
        db.EvaluationProfiles.Add(profile);

        _locked = Period("Q3-LOCKED", PeriodStatus.Locked, now);
        _open = Period("Q4-OPEN", PeriodStatus.Open, now);
        db.EvaluationPeriods.AddRange(_locked, _open);

        _hr = Person("HR", _sales.Id, job.Id, now);
        _outsider = Person("OUTSIDE", _sales.Id, job.Id, now);
        var a = Person("NV-A", _sales.Id, job.Id, now);
        var b = Person("NV-B", _sales.Id, job.Id, now);
        var c = Person("NV-C", _sales.Id, job.Id, now);
        db.Accounts.AddRange(_hr, _outsider, a, b, c);

        // The HR_OFFICER role and RANKING_RECORD_READ grant come from the migrations, not from this test.
        var hrRole = await db.Roles.SingleAsync(r => r.Code == "HR_OFFICER");
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = _hr.Id, RoleId = hrRole.Id, UpdatedAt = now });

        _top = Evaluation(a, _locked, profile, 96m, PerformanceEvaluationStatus.Final, now, "<!-- APPROVAL_COMMENTS: Phê duyệt -->");
        var second = Evaluation(b, _locked, profile, 88m, PerformanceEvaluationStatus.Final, now, null);
        _inReview = Evaluation(c, _locked, profile, 70m, PerformanceEvaluationStatus.InReview, now, null);
        db.PerformanceEvaluations.AddRange(_top, second, _inReview);
        await db.SaveChangesAsync();
    }

    private static EvaluationPeriod Period(string code, PeriodStatus status, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = $"Period {code}",
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
        PeriodType = PeriodType.Year,
        Status = status,
        UpdatedAt = now,
    };

    private static PerformanceEvaluation Evaluation(
        Account account,
        EvaluationPeriod period,
        EvaluationProfile profile,
        decimal score,
        PerformanceEvaluationStatus status,
        DateTimeOffset now,
        string? comment) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = account.EmployeeId,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = profile.Id,
            TotalScore = score,
            Status = status,
            ApprovedAt = now,
            Comment = comment,
            UpdatedAt = now,
        };

    private static Account Person(string code, Guid departmentId, Guid jobId, DateTimeOffset now)
    {
        var employee = new Employee
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
        };
        return new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = code,
            PasswordHash = "unused-http-test-hash",
            LastSyncAt = now,
            UpdatedAt = now,
            Employee = employee,
        };
    }
}
