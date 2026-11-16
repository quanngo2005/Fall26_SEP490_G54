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
/// End-to-end HTTP + PostgreSQL + Redis verification for Employee Import (<c>02_Nhan_su_To_chuc.xlsx</c>)
/// and Transaction Import (<c>06_Giao_dich.xlsx</c>) against real Kestrel + DbMigrator-equivalent seed.
/// </summary>
public sealed class ImportSamplesHttpTests : IAsyncLifetime, IAsyncDisposable
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private HttpClient _client = null!;
    private System.Diagnostics.Process _api = null!;
    private readonly StringBuilder _apiLog = new();
    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private Account _sysAdmin = null!;
    private Account _staff = null!;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin || TestRedis.ConnectionString is not { } redis)
        {
            throw new InvalidOperationException("HTTP verification requires PostgreSQL and Redis.");
        }

        _database = $"g54_import_http_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(connString);
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddScoped(provider => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .EnableServiceProviderCaching(false).Options);
        _provider = services.BuildServiceProvider();

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");

            // Lock period 09/2026 so row 21 (28/09/2026) of 06_Giao_dich.xlsx triggers BR-29 PERIOD_LOCKED.
            var sep2026 = await db.EvaluationPeriods.FirstOrDefaultAsync(
                p => p.StartDate == new DateOnly(2026, 9, 1) && p.EndDate == new DateOnly(2026, 9, 30));
            if (sep2026 is not null)
            {
                sep2026.Status = PeriodStatus.Locked;
            }
            else
            {
                db.EvaluationPeriods.Add(new EvaluationPeriod
                {
                    Id = Guid.NewGuid(),
                    Code = "M-2026-09",
                    Name = "Tháng 09/2026",
                    StartDate = new DateOnly(2026, 9, 1),
                    EndDate = new DateOnly(2026, 9, 30),
                    PeriodType = PeriodType.Month,
                    Status = PeriodStatus.Locked,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync();

            _sysAdmin = await db.Accounts.Include(a => a.Employee).SingleAsync(a => a.Employee.EmployeeCode == "NV013");
            _staff = await db.Accounts.Include(a => a.Employee).SingleAsync(a => a.Employee.EmployeeCode == "NV004");
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
        start.Environment["ConnectionStrings__Postgres"] = connString;
        start.Environment["ConnectionStrings__Redis"] = redis;
        start.Environment["Jwt__Key"] = _jwtKey;
        start.Environment["Jwt__Issuer"] = "ImportSamplesHttpTests";
        start.Environment["Jwt__Audience"] = "ImportSamplesHttpTests";

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
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    [SampleFileFact("02_Nhan_su_To_chuc.xlsx", "06_Giao_dich.xlsx")]
    public async Task RealSamples_EmployeePreviewCommitAndTransactionImport_SucceedAndEnforceAuthOverHttp()
    {
        var employeeSamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "02_Nhan_su_To_chuc.xlsx");
        var transactionSamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "06_Giao_dich.xlsx");

        // 1. Unauthenticated and Forbidden checks
        _client.DefaultRequestHeaders.Authorization = null;
        using (var unauthRes = await PostFileAsync("/data-imports/employees/preview", employeeSamplePath))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);
        }

        Authenticate(_staff);
        using (var forbiddenEmp = await PostFileAsync("/data-imports/employees/preview", employeeSamplePath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenEmp.StatusCode);
        }

        using (var forbiddenTx = await PostFileAsync("/transaction-imports", transactionSamplePath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenTx.StatusCode);
        }

        // 2. System Admin: Templates download
        Authenticate(_sysAdmin);
        using (var empTpl = await _client.GetAsync("/data-imports/employees/template"))
        {
            Assert.Equal(HttpStatusCode.OK, empTpl.StatusCode);
            Assert.NotEmpty(await empTpl.Content.ReadAsByteArrayAsync());
        }

        using (var txTpl = await _client.GetAsync("/transaction-imports/template"))
        {
            Assert.Equal(HttpStatusCode.OK, txTpl.StatusCode);
            Assert.NotEmpty(await txTpl.Content.ReadAsByteArrayAsync());
        }

        // 3. Employee preview on real 02_Nhan_su_To_chuc.xlsx
        using (var previewRes = await PostFileAsync("/data-imports/employees/preview", employeeSamplePath))
        {
            var previewJson = await ReadEnvelopeAsync(previewRes, HttpStatusCode.OK);
            var summary = previewJson.GetProperty("data").GetProperty("summary");
            Assert.Equal(21, summary.GetProperty("totalRows").GetInt32());
            Assert.Equal(17, summary.GetProperty("valid").GetInt32());
            Assert.Equal(4, summary.GetProperty("invalid").GetInt32());
        }

        // 4. Employee commit on real 02_Nhan_su_To_chuc.xlsx (updates NV001..NV020 including setting NV006 = TERMINATED)
        using (var commitRes = await PostFileAsync("/data-imports/employees/commit", employeeSamplePath))
        {
            var commitJson = await ReadEnvelopeAsync(commitRes, HttpStatusCode.OK);
            Assert.Equal("no-store", commitRes.Headers.CacheControl?.ToString());
            var commitData = commitJson.GetProperty("data");
            Assert.Equal(17, commitData.GetProperty("created").GetInt32() + commitData.GetProperty("updated").GetInt32());
            Assert.Equal(4, commitData.GetProperty("skipped").GetInt32());
            Assert.Equal(4, commitData.GetProperty("errors").GetArrayLength());
        }

        // 5. Transaction import on real 06_Giao_dich.xlsx (18 valid, 5 invalid on rows 20..24)
        Guid batchId;
        using (var txRes = await PostFileAsync("/transaction-imports", transactionSamplePath))
        {
            var txJson = await ReadEnvelopeAsync(txRes, HttpStatusCode.Created);
            var txData = txJson.GetProperty("data");
            batchId = txData.GetProperty("id").GetGuid();
            Assert.Equal(23, txData.GetProperty("totalRows").GetInt32());
            Assert.Equal(18, txData.GetProperty("importedRows").GetInt32());
            Assert.Equal(0, txData.GetProperty("duplicateRows").GetInt32());
            Assert.Equal(5, txData.GetProperty("errorRows").GetInt32());

            var errors = txData.GetProperty("errors").EnumerateArray().OrderBy(e => e.GetProperty("row").GetInt32()).ToList();
            Assert.Equal([20, 21, 22, 23, 24], errors.Select(e => e.GetProperty("row").GetInt32()));
            Assert.Equal("DUPLICATE_IN_FILE", errors[0].GetProperty("code").GetString());
            Assert.Equal("PERIOD_LOCKED", errors[1].GetProperty("code").GetString());
            Assert.Equal("EMPLOYEE_NOT_FOUND", errors[2].GetProperty("code").GetString());
            Assert.Equal("INVALID_VALUE", errors[3].GetProperty("code").GetString());
            Assert.Equal("EMPLOYEE_TERMINATED", errors[4].GetProperty("code").GetString());
        }

        // 6. Verify GET /transaction-imports/{id}/transactions and /{id}/errors + 404
        using (var txListRes = await _client.GetAsync($"/transaction-imports/{batchId}/transactions?page=1&pageSize=20"))
        {
            var txListJson = await ReadEnvelopeAsync(txListRes, HttpStatusCode.OK);
            var firstItem = txListJson.GetProperty("data").GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("transactionCode").GetString() == "GD2610001");
            Assert.Equal("2026-10-05", firstItem.GetProperty("transactionDate").GetString());
            Assert.Equal("KH10021", firstItem.GetProperty("customerCode").GetString());
            Assert.Equal(15000000m, firstItem.GetProperty("amount").GetDecimal());
        }

        using (var errListRes = await _client.GetAsync($"/transaction-imports/{batchId}/errors?page=1&pageSize=20"))
        {
            var errListJson = await ReadEnvelopeAsync(errListRes, HttpStatusCode.OK);
            Assert.Equal(5, errListJson.GetProperty("data").GetProperty("totalCount").GetInt32());
        }

        using (var notFoundRes = await _client.GetAsync($"/transaction-imports/{Guid.NewGuid()}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);
        }
    }

    private async Task<HttpResponseMessage> PostFileAsync(string url, string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        return await _client.PostAsync(url, content);
    }

    private void Authenticate(Account account)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = "ImportSamplesHttpTests",
            ["Jwt:Audience"] = "ImportSamplesHttpTests",
        }).Build();
        var token = new JwtTokenProvider(configuration).CreateAccessToken(
            account.Id, account.Employee.Email ?? account.Username, account.Employee.FullName, [], [], TimeSpan.FromMinutes(10));
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

    private string SnapshotLog()
    {
        lock (_apiLog)
        {
            return _apiLog.ToString();
        }
    }

    private async Task<JsonElement> ReadEnvelopeAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}\nAPI LOG:\n{SnapshotLog()}");
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        return root.Clone();
    }
}
