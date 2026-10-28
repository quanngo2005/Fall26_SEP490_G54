using System.Diagnostics;
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

/// <summary>Exercises the real API pipeline, JWT, permission service, PostgreSQL triggers and Redis cache.</summary>
public sealed class AppealHttpTests : IAsyncLifetime, IAsyncDisposable
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _maker = null!;
    private Account _checker = null!;
    private Account _outsider = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_appeal_http_{Guid.NewGuid():N}";
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
        await SeedPeopleAsync(db);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var address = new Uri($"http://127.0.0.1:{port}");
        var start = new ProcessStartInfo("dotnet")
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
        start.Environment["Jwt__Issuer"] = "AppealHttpTests";
        start.Environment["Jwt__Audience"] = "AppealHttpTests";
        _api = Process.Start(start) ?? throw new InvalidOperationException("Could not start the test API.");
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
    public async Task Decisions_ThroughHttp_EnforceValidationPermissionsAndPersistExpectedStates()
    {
        using var health = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var accept = await AddRejectedSubmissionAsync();
        using var noToken = await _client.PostAsJsonAsync($"/appeals/{Guid.NewGuid()}/decision", new { decision = "ACCEPT" });
        await CheckAsync(noToken, HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        Authenticate(_maker);
        var acceptAppeal = await FileAppealAsync(accept);
        using var noPermission = await _client.PostAsJsonAsync($"/appeals/{acceptAppeal}/decision", new { decision = "ACCEPT" });
        await CheckAsync(noPermission, HttpStatusCode.Forbidden, "FORBIDDEN");

        Authenticate(_outsider);
        using var outside = await _client.PostAsJsonAsync($"/appeals/{acceptAppeal}/decision", new { decision = "ACCEPT" });
        await CheckAsync(outside, HttpStatusCode.Forbidden, "FORBIDDEN");

        Authenticate(_checker);
        using var invalid = await _client.PostAsJsonAsync($"/appeals/{acceptAppeal}/decision", new { decision = "UNKNOWN" });
        await CheckAsync(invalid, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        using var missingNote = await _client.PostAsJsonAsync($"/appeals/{acceptAppeal}/decision", new { decision = "ADJUST" });
        await CheckAsync(missingNote, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        using var negative = await _client.PostAsJsonAsync($"/appeals/{acceptAppeal}/decision", new { decision = "ACCEPT", verifiedValue = -1 });
        await CheckAsync(negative, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        using var missing = await _client.PostAsJsonAsync($"/appeals/{Guid.NewGuid()}/decision", new { decision = "ACCEPT" });
        await CheckAsync(missing, HttpStatusCode.NotFound, "NOT_FOUND");
        await DecideAsync(acceptAppeal, "ACCEPT", "APPROVED", "VERIFIED");
        await CheckStoredAsync(acceptAppeal, accept, AppealStatus.Accepted, SubmissionStatus.Approved, WorkStatus.Verified, 1);
        using var repeat = await _client.PostAsJsonAsync($"/appeals/{acceptAppeal}/decision", new { decision = "ACCEPT" });
        await CheckAsync(repeat, HttpStatusCode.Conflict, "APPEAL_INVALID_STATE");

        var kpiSubmission = await AddRejectedSubmissionAsync();
        await AddKpiTargetAsync(kpiSubmission);
        Authenticate(_maker);
        var kpiAppeal = await FileAppealAsync(kpiSubmission);
        Authenticate(_checker);
        using var kpiResponse = await _client.PostAsJsonAsync($"/appeals/{kpiAppeal}/decision", new { decision = "ACCEPT", verifiedValue = 12.5m });
        await CheckAsync(kpiResponse, HttpStatusCode.OK);
        await using (var kpiScope = _provider.CreateAsyncScope())
        {
            var db = kpiScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.WorkResultItems.AsNoTracking().SingleAsync(i => i.VerifiedWorkResult.Verification.ResultSubmissionId == kpiSubmission);
            Assert.Equal(_maker.EmployeeId, item.EmployeeId);
            Assert.Equal(12.5m, item.Value);
            Assert.Equal(12.5m, (await db.ActualProgresses.AsNoTracking().SingleAsync(p => p.Id == item.ActualProgressId)).Value);
        }

        await CheckScoreHoldHttpAsync();

        var adjust = await AddRejectedSubmissionAsync();
        Authenticate(_maker);
        var adjustAppeal = await FileAppealAsync(adjust);
        Authenticate(_checker);
        await DecideAsync(adjustAppeal, "ADJUST", "UNDER_REVIEW", "IN_PROGRESS");
        await CheckStoredAsync(adjustAppeal, adjust, AppealStatus.Accepted, SubmissionStatus.UnderReview, WorkStatus.InProgress, 0);
        using var history = await _client.GetAsync($"/verifications/{adjust}/history");
        var historyBody = await CheckAsync(history, HttpStatusCode.OK);
        Assert.Equal("REVISION_REQUIRED", historyBody.GetProperty("data")[1].GetProperty("decision").GetString());
        // The adjusted submission must remain reviewable by the ordinary verification flow.
        using var approveAfterAdjust = await _client.PostAsJsonAsync($"/verifications/{adjust}/decision", new { decision = "APPROVED" });
        await CheckAsync(approveAfterAdjust, HttpStatusCode.OK);
        await CheckStoredAsync(adjustAppeal, adjust, AppealStatus.Accepted, SubmissionStatus.Approved, WorkStatus.Verified, 1);

        var reject = await AddRejectedSubmissionAsync();
        Authenticate(_maker);
        var rejectAppeal = await FileAppealAsync(reject);
        Authenticate(_checker);
        await DecideAsync(rejectAppeal, "REJECT", "REJECTED", "REJECTED");
        await CheckStoredAsync(rejectAppeal, reject, AppealStatus.Rejected, SubmissionStatus.Rejected, WorkStatus.Rejected, 0);

        Authenticate(_maker);
        using var detail = await _client.GetAsync($"/appeals/{rejectAppeal}");
        var detailBody = await CheckAsync(detail, HttpStatusCode.OK);
        Assert.Equal("REJECTED", detailBody.GetProperty("data").GetProperty("status").GetString());
        using var list = await _client.GetAsync("/appeals?status=ACCEPTED&page=1&pageSize=20");
        var listBody = await CheckAsync(list, HttpStatusCode.OK);
        Assert.Equal(4, listBody.GetProperty("data").GetProperty("totalCount").GetInt32());

        // Race a withdrawal against acceptance using separate HTTP requests/DbContexts.
        var raced = await AddRejectedSubmissionAsync();
        var racedAppeal = await FileAppealAsync(raced);
        using var withdrawRequest = new HttpRequestMessage(HttpMethod.Post, $"/appeals/{racedAppeal}/withdraw");
        withdrawRequest.Headers.Authorization = _client.DefaultRequestHeaders.Authorization;
        Authenticate(_checker);
        var withdrawTask = _client.SendAsync(withdrawRequest);
        var decideTask = _client.PostAsJsonAsync($"/appeals/{racedAppeal}/decision", new { decision = "ACCEPT" });
        using var withdrawResponse = await withdrawTask;
        using var decideResponse = await decideTask;
        Assert.True(withdrawResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, GetApiLog());
        Assert.True(decideResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, GetApiLog());
        Assert.NotEqual(withdrawResponse.StatusCode == HttpStatusCode.OK, decideResponse.StatusCode == HttpStatusCode.OK);
        await using (var raceScope = _provider.CreateAsyncScope())
        {
            var db = raceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedAppeal = await db.Appeals.AsNoTracking().SingleAsync(a => a.Id == racedAppeal);
            var storedSubmission = await db.ResultSubmissions.AsNoTracking().SingleAsync(s => s.Id == raced);
            Assert.Equal(storedAppeal.Status == AppealStatus.Accepted ? SubmissionStatus.Approved : SubmissionStatus.Rejected, storedSubmission.Status);
        }

        // Cached permissions or a still-valid JWT must not let a locked account resolve appeals.
        var lockedSubmission = await AddRejectedSubmissionAsync();
        Authenticate(_maker);
        var lockedAppeal = await FileAppealAsync(lockedSubmission);
        await using (var lockScope = _provider.CreateAsyncScope())
        {
            var db = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var checker = await db.Accounts.SingleAsync(a => a.Id == _checker.Id);
            checker.IsLocked = true;
            await db.SaveChangesAsync();
        }

        Authenticate(_checker);
        using var lockedResponse = await _client.PostAsJsonAsync($"/appeals/{lockedAppeal}/decision", new { decision = "ACCEPT" });
        await CheckAsync(lockedResponse, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    private void Authenticate(Account account, string[]? roles = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "AppealHttpTests",
            ["Jwt:Audience"] = "AppealHttpTests",
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

    private string GetApiLog()
    {
        lock (_apiLog)
        {
            return _apiLog.ToString();
        }
    }

    private async Task<Guid> FileAppealAsync(Guid submissionId)
    {
        using var form = new MultipartFormDataContent { { new StringContent("Đề nghị xem xét lại kết quả đã nộp"), "reason" } };
        using var response = await _client.PostAsync($"/submissions/{submissionId}/appeals", form);
        var body = await CheckAsync(response, HttpStatusCode.Created);
        Assert.NotNull(response.Headers.Location);
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task DecideAsync(Guid appealId, string decision, string submissionStatus, string workStatus)
    {
        using var response = await _client.PostAsJsonAsync($"/appeals/{appealId}/decision",
            new { decision, note = "Bổ sung chứng từ đối soát có chữ ký xác nhận" });
        var body = await CheckAsync(response, HttpStatusCode.OK);
        var data = body.GetProperty("data");
        Assert.Equal(decision, data.GetProperty("decision").GetString());
        Assert.Equal(submissionStatus, data.GetProperty("submissionStatus").GetString());
        Assert.Equal(workStatus, data.GetProperty("workStatus").GetString());
        Assert.Equal(_checker.EmployeeId, data.GetProperty("appeal").GetProperty("resolvedByEmployeeId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("appeal").GetProperty("decidedAt").ValueKind);
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

    private async Task CheckStoredAsync(Guid appealId, Guid submissionId, AppealStatus appealStatus,
        SubmissionStatus submissionStatus, WorkStatus workStatus, int resultCount)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appeal = await db.Appeals.AsNoTracking().SingleAsync(a => a.Id == appealId);
        var submission = await db.ResultSubmissions.AsNoTracking().Include(s => s.Work).SingleAsync(s => s.Id == submissionId);
        Assert.Equal(appealStatus, appeal.Status);
        Assert.Equal(_checker.EmployeeId, appeal.ResolvedBy);
        Assert.Equal(submissionStatus, submission.Status);
        Assert.Equal(workStatus, submission.Work.Status);
        Assert.Equal(resultCount, await db.VerifiedWorkResults.CountAsync(r => r.Verification.ResultSubmissionId == submissionId));
    }

    private async Task<Guid> AddRejectedSubmissionAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"HTTP-{Guid.NewGuid():N}"[..20],
            Title = "Appeal HTTP test",
            AssigneeEmployeeId = _maker.EmployeeId,
            CreatedBy = _maker.EmployeeId,
            Status = WorkStatus.Rejected,
            Priority = WorkPriority.Medium,
            UpdatedAt = now,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = _maker.EmployeeId,
            SubmissionNo = 1,
            Status = SubmissionStatus.Rejected,
            SubmittedAt = now,
            UpdatedAt = now,
        };
        db.Works.Add(work);
        db.ResultSubmissions.Add(submission);
        db.Verifications.Add(new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = _checker.EmployeeId,
            RoundNo = 1,
            Decision = VerificationDecision.Rejected,
            VerifiedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return submission.Id;
    }

    private async Task SeedPeopleAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var department = new Department { Id = Guid.NewGuid(), Code = "D", Name = "Department", LevelType = DepartmentLevelType.Department };
        var other = new Department { Id = Guid.NewGuid(), Code = "OTHER", Name = "Other", LevelType = DepartmentLevelType.Department };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "J", Title = "Job" };
        db.Departments.AddRange(department, other);
        db.JobPositions.Add(job);
        _maker = Person("MAKER", department.Id, job.Id, now);
        _checker = Person("CHECKER", department.Id, job.Id, now);
        _outsider = Person("OUTSIDE", other.Id, job.Id, now);
        db.Accounts.AddRange(_maker, _checker, _outsider);
        var role = new Role { Id = Guid.NewGuid(), Code = "CHECKER", Name = "Checker", Status = RecordStatus.Active };
        var permission = new PermissionRecord { Id = Guid.NewGuid(), Code = "VERIFICATION_DECIDE", Name = "Decide", Resource = "verification", Action = "decide" };
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermissionAssignment { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permission.Id, Scope = "Dept" });
        db.UserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), AccountId = _checker.Id, RoleId = role.Id },
            new UserRole { Id = Guid.NewGuid(), AccountId = _outsider.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    private async Task AddKpiTargetAsync(Guid submissionId)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group" };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI",
            Name = "KPI",
            Unit = "Điểm",
            Status = KpiStatus.Active,
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
        };
        var type = new WorkType { Id = Guid.NewGuid(), Code = "WT", Name = "Work type", Status = RecordStatus.Active };
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "P",
            Name = "Period",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(today.Year, 1, 1),
            EndDate = new DateOnly(today.Year, 12, 31),
            Status = PeriodStatus.Open,
        };
        db.KpiGroups.Add(group);
        db.KpiDefinitions.Add(kpi);
        db.WorkTypes.Add(type);
        db.EvaluationPeriods.Add(period);
        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = _maker.Employee.JobPositionId,
            Code = "HTTP-PROFILE",
            Name = "Profile",
            Version = 1,
            EffectiveFrom = period.StartDate,
            Status = EvaluationProfileStatus.Active,
        };
        db.EvaluationProfiles.Add(profile);
        db.EvaluationProfileItems.Add(new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiGroupId = group.Id,
            WeightPercent = 100m,
            DisplayOrder = 1,
        });
        db.PerformanceEvaluations.Add(new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = _maker.EmployeeId,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = profile.Id,
            TotalScore = 100m,
            Status = PerformanceEvaluationStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.WorkTypeKpis.Add(new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = type.Id,
            KpiDefinitionId = kpi.Id,
            EffectiveFrom = period.StartDate,
        });
        db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = _maker.EmployeeId,
            EvaluationPeriodId = period.Id,
            Version = 1,
            TargetValue = 100,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ApprovedAt = DateTimeOffset.UtcNow,
            AssignedAt = DateTimeOffset.UtcNow,
            Status = TargetAssignmentStatus.Active,
        });
        var submission = await db.ResultSubmissions.Include(s => s.Work).SingleAsync(s => s.Id == submissionId);
        submission.Work.WorkTypeId = type.Id;
        await db.SaveChangesAsync();
    }

    private async Task CheckScoreHoldHttpAsync()
    {
        var submissionId = await AddRejectedSubmissionAsync();
        Authenticate(_maker);
        var appealId = await FileAppealAsync(submissionId);
        using var unauthorizedReview = await _client.PostAsJsonAsync($"/appeals/{appealId}/review", new { points = 9, reason = "Tạm giữ điểm trong lúc xác minh" });
        await CheckAsync(unauthorizedReview, HttpStatusCode.Forbidden, "FORBIDDEN");
        Authenticate(_checker);
        using var adminOnly = await _client.PutAsJsonAsync("/system-parameters/appeal-score-hold", new { defaultPoints = 9 });
        await CheckAsync(adminOnly, HttpStatusCode.Forbidden, "FORBIDDEN");
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = new Role { Id = Guid.NewGuid(), Code = "ADMIN", Name = "Admin", Status = RecordStatus.Active };
            db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = _checker.Id, Role = role });
            await db.SaveChangesAsync();
        }

        Authenticate(_checker, ["ADMIN"]);
        using var setting = await _client.PutAsJsonAsync("/system-parameters/appeal-score-hold", new { defaultPoints = 9 });
        await CheckAsync(setting, HttpStatusCode.OK);
        using var review = await _client.PostAsJsonAsync($"/appeals/{appealId}/review", new { reason = "Tạm giữ điểm trong lúc xác minh" });
        var reviewBody = await CheckAsync(review, HttpStatusCode.OK);
        Assert.Equal(-9, reviewBody.GetProperty("data").GetProperty("points").GetDecimal());
        Assert.Equal("SYSTEM", reviewBody.GetProperty("data").GetProperty("source").GetString());
        using var repeatedReview = await _client.PostAsJsonAsync($"/appeals/{appealId}/review", new { reason = "Không được trừ thêm lần thứ hai" });
        await CheckAsync(repeatedReview, HttpStatusCode.Conflict, "APPEAL_INVALID_STATE");
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(91, (await db.PerformanceEvaluations.SingleAsync()).TotalScore);
            var hold = await db.ScoreEntries.SingleAsync();
            hold.Reason = "Không được sửa lịch sử";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        Authenticate(_maker);
        using var dispute = await _client.PostAsJsonAsync($"/appeals/{appealId}/score-dispute", new { reason = "Tôi đề nghị xem xét lại khoản giữ điểm" });
        var disputeBody = await CheckAsync(dispute, HttpStatusCode.OK);
        Assert.Equal("Tôi đề nghị xem xét lại khoản giữ điểm", disputeBody.GetProperty("data").GetProperty("scoreDisputeReason").GetString());
        Authenticate(_checker, ["ADMIN"]);
        await DecideAsync(appealId, "ACCEPT", "APPROVED", "VERIFIED");
        Authenticate(_maker);
        using var entries = await _client.GetAsync($"/appeals/{appealId}/score-entries");
        var entryBody = await CheckAsync(entries, HttpStatusCode.OK);
        Assert.Equal(2, entryBody.GetProperty("data").GetArrayLength());
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(100, (await db.PerformanceEvaluations.SingleAsync()).TotalScore);
            Assert.Equal(0, await db.ScoreEntries.SumAsync(e => e.Points));
        }
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
