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

namespace G54.UnitTests.Integration.Import;

/// <summary>Skips an import integration test when PostgreSQL or Redis is not available (fails on CI, see <see cref="TestPostgres"/>).</summary>
public sealed class ImportIntegrationFactAttribute : FactAttribute
{
    public ImportIntegrationFactAttribute()
    {
        if (!ImportApiFixture.InfrastructureAvailable)
        {
            Skip = $"{TestPostgres.SkipReason} {TestRedis.SkipReason}";
        }
    }
}

/// <summary>Theory variant of <see cref="ImportIntegrationFactAttribute"/>.</summary>
public sealed class ImportIntegrationTheoryAttribute : TheoryAttribute
{
    public ImportIntegrationTheoryAttribute()
    {
        if (!ImportApiFixture.InfrastructureAvailable)
        {
            Skip = $"{TestPostgres.SkipReason} {TestRedis.SkipReason}";
        }
    }
}

/// <summary>
/// One real API process (Kestrel) on a fresh PostgreSQL database seeded like DbMigrator (<see cref="DatabaseSeeder"/>),
/// plus Redis. One instance per test class (<c>IClassFixture</c>), so each class has its own database.
/// Seeded accounts used here: <c>NV013</c> system admin, <c>NV004</c>/<c>NV005</c> employees.
/// </summary>
public sealed class ImportApiFixture : IAsyncLifetime
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Issuer = "ImportIntegrationTests";
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly StringBuilder _apiLog = new();
    private string _database = null!;
    private ServiceProvider? _provider;
    private System.Diagnostics.Process? _api;

    public static bool InfrastructureAvailable =>
        TestPostgres.AdminConnectionString is not null && TestRedis.ConnectionString is not null;

    public HttpClient Client { get; private set; } = null!;


    public async Task InitializeAsync()
    {
        if (!InfrastructureAvailable)
        {
            return;
        }

        var admin = TestPostgres.AdminConnectionString!;
        _database = $"g54_import_it_{Guid.NewGuid():N}";
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
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
        }

        await StartApiAsync(connectionString, TestRedis.ConnectionString!);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
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

    // ---- database ------------------------------------------------------------------------------------

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = _provider!.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = _provider!.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Stores an evaluation period with a unique code and returns its id.</summary>
    public async Task<Guid> AddPeriodAsync(PeriodType type, DateOnly start, DateOnly end, PeriodStatus status, Guid? parentId = null)
    {
        var id = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.EvaluationPeriods.Add(new EvaluationPeriod
            {
                Id = id,
                Code = $"IT-{type}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                Name = $"Integration {type} {start:yyyy-MM-dd}",
                PeriodType = type,
                ParentPeriodId = parentId,
                StartDate = start,
                EndDate = end,
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    public Task SetPeriodStatusAsync(Guid periodId, PeriodStatus status) => WithDbAsync(async db =>
    {
        var period = await db.EvaluationPeriods.SingleAsync(p => p.Id == periodId);
        period.Status = status;
        period.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    });

    public Task SetEmployeeStatusAsync(string employeeCode, EmployeeStatus status) => WithDbAsync(async db =>
    {
        var employee = await db.Employees.SingleAsync(e => e.EmployeeCode == employeeCode);
        employee.Status = status;
        await db.SaveChangesAsync();
    });

    // ---- HTTP ----------------------------------------------------------------------------------------

    /// <summary>Posts an .xlsx as multipart <c>file</c>. <paramref name="who"/> is an employee code, or <c>null</c> for anonymous.</summary>
    public async Task<ApiResult> PostFileAsync(string url, byte[] bytes, string? who, string fileName = "import.xlsx", string contentType = XlsxContentType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        await AuthorizeAsync(request, who);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        request.Content = content;
        return await SendCoreAsync(request);
    }

    public async Task<ApiResult> SendAsync(HttpMethod method, string url, string? who, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        await AuthorizeAsync(request, who);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, CamelCase),
                Encoding.UTF8,
                "application/json");
        }

        return await SendCoreAsync(request);
    }

    public async Task<HttpResponseMessage> GetRawAsync(string url, string? who)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AuthorizeAsync(request, who);
        return await Client.SendAsync(request);
    }

    /// <summary>Fails with the API log attached when the status is not <paramref name="expected"/>.</summary>
    public void AssertStatus(ApiResult result, HttpStatusCode expected) =>
        Assert.True(result.Status == expected, $"Expected {(int)expected}, got {(int)result.Status}: {result.Root}\nAPI LOG (tail):\n{LogTail()}");

    private async Task<ApiResult> SendCoreAsync(HttpRequestMessage request)
    {
        using var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement root = default;
        if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('{'))
        {
            using var document = JsonDocument.Parse(text);
            root = document.RootElement.Clone();
        }

        var result = new ApiResult(response.StatusCode, root, response.Headers.Location);
        return result;
    }

    private async Task AuthorizeAsync(HttpRequestMessage request, string? who)
    {
        if (who is null)
        {
            return;
        }

        var account = await QueryAsync(db => db.Accounts.AsNoTracking().Include(a => a.Employee)
            .SingleAsync(a => a.Employee.EmployeeCode == who));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = _jwtKey,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Issuer,
        }).Build();
        var token = new JwtTokenProvider(configuration).CreateAccessToken(
            account.Id, account.Employee.Email ?? account.Username, account.Employee.FullName, [], [], TimeSpan.FromMinutes(10));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }

    // ---- API process ---------------------------------------------------------------------------------

    private async Task StartApiAsync(string connectionString, string redis)
    {
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
        Client = new HttpClient { BaseAddress = address };

        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (_api.HasExited)
            {
                throw new InvalidOperationException($"Test API exited with {_api.ExitCode}: {LogTail()}");
            }

            try
            {
                using var ready = await Client.GetAsync("/health/ready");
                if (ready.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel has not bound its port yet.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Test API did not become ready.");
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

    private string LogTail()
    {
        lock (_apiLog)
        {
            var text = _apiLog.ToString();
            return text.Length > 30000 ? text[^30000..] : text;
        }
    }
}
