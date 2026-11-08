using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Runs the verification queue on migrated PostgreSQL: the keyword search (<c>ILike</c>), the enum parameters and the
/// match-status subqueries are translated by Npgsql, which the in-memory provider cannot show.
/// </summary>
public sealed class VerificationQueuePostgresTests : IAsyncLifetime
{
    private string _database = null!;
    private ServiceProvider? _provider;

    private sealed record Seeded(Guid VerifierAccountId, string Tag, Guid SubmitterId);

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_vqueue_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDataAccess(new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString);
        // This class owns a unique data source; do not retain its EF provider in the process-wide cache.
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddScoped(provider => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .EnableServiceProviderCaching(false).Options);
        _provider = services.BuildServiceProvider();
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
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

    private async Task<T> RunAsync<T>(Func<VerificationService, Task<T>> action)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var permissions = new FakePermissionService(null, new UserPermissionDto("verification", "decide", "All", "VERIFICATION_DECIDE"));
        return await action(new VerificationService(db, NullLogger<VerificationService>.Instance, TimeProvider.System, permissions));
    }

    /// <summary>One department with a verifier and a submitter, plus works that carry a unique tag in their code.</summary>
    private async Task<Seeded> SeedAsync(params (string Title, string[] Transactions)[] works)
    {
        var tag = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using var scope = _provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = $"D{tag}",
            Name = $"Phòng {tag}",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = $"P{tag}", Title = "Chuyên viên", Status = RecordStatus.Active, UpdatedAt = now };
        db.Departments.Add(department);
        db.JobPositions.Add(position);

        Employee NewEmployee(string code) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Nhân viên {code}",
            Email = $"{code.ToLowerInvariant()}@queue.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var verifier = NewEmployee($"V{tag}");
        var submitter = NewEmployee($"S{tag}");
        db.Employees.AddRange(verifier, submitter);
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = verifier.Id,
            Username = verifier.Email,
            PasswordHash = "hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Accounts.Add(account);

        for (var w = 0; w < works.Length; w++)
        {
            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = $"WRK-{tag}-{w}",
                Title = works[w].Title,
                AssigneeEmployeeId = submitter.Id,
                CreatedBy = submitter.Id,
                Status = WorkStatus.Submitted,
                Priority = WorkPriority.Medium,
                UpdatedAt = now,
            };
            db.Works.Add(work);
            db.ResultSubmissions.Add(new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                SubmitterEmployeeId = submitter.Id,
                SubmissionNo = 1,
                Status = SubmissionStatus.Submitted,
                SubmittedAt = now.AddMinutes(w),
                UpdatedAt = now,
            });
            for (var t = 0; t < works[w].Transactions.Length; t++)
            {
                db.WorkTransactions.Add(new WorkTransaction
                {
                    Id = Guid.NewGuid(),
                    WorkId = work.Id,
                    TransactionCode = $"{tag}-{w}-{t}",
                    Status = works[w].Transactions[t],
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync();
        return new Seeded(account.Id, tag, submitter.Id);
    }

    [PostgresFact]
    public async Task GetQueue_KeywordIsCaseInsensitiveAndTreatsWildcardsLiterally()
    {
        var seeded = await SeedAsync(("Đạt 100% chỉ tiêu", []), ("Đạt 1000 chỉ tiêu", []), ("Mã a_b", []), ("Mã axb", []));

        async Task<VerificationQueueItemDto[]> Search(string keyword) =>
            (await RunAsync(s => s.GetQueueAsync(seeded.VerifierAccountId, new VerificationQueueFilterDto { Keyword = keyword }))).Items.ToArray();

        Assert.Equal("WRK-" + seeded.Tag + "-0", Assert.Single(await Search("100%")).WorkCode);
        Assert.Equal("WRK-" + seeded.Tag + "-2", Assert.Single(await Search("a_b")).WorkCode);
        Assert.Equal(4, (await Search(seeded.Tag.ToLowerInvariant())).Length);
        Assert.Equal(4, (await Search($"  s{seeded.Tag.ToLowerInvariant()} ")).Length);
        Assert.Single(await Search("ĐẠT 1000"));
        Assert.Empty(await Search("không-có-từ-này-" + seeded.Tag));
    }

    [PostgresFact]
    public async Task GetQueue_MatchStatusFilterAndPaging_RunOnPostgres()
    {
        var seeded = await SeedAsync(
            ("không mã", []),
            ("khớp hết", [WorkTransactionStatus.Matched, WorkTransactionStatus.Matched]),
            ("có mã lệch", [WorkTransactionStatus.Matched, WorkTransactionStatus.NotMatched]),
            ("chờ dữ liệu", [WorkTransactionStatus.Matched, WorkTransactionStatus.InReview]));

        async Task<VerificationQueueItemDto[]> Filter(string matchStatus) =>
            (await RunAsync(s => s.GetQueueAsync(seeded.VerifierAccountId, new VerificationQueueFilterDto { Keyword = seeded.Tag, MatchStatus = matchStatus }))).Items.ToArray();

        Assert.Equal("không mã", (await Filter("NONE")).Single().WorkTitle);
        Assert.Equal("khớp hết", (await Filter("MATCHED")).Single().WorkTitle);
        Assert.Equal("có mã lệch", (await Filter("NOT_MATCHED")).Single().WorkTitle);
        var waiting = (await Filter("IN_REVIEW")).Single();
        Assert.Equal("chờ dữ liệu", waiting.WorkTitle);
        Assert.Equal(2, waiting.TransactionCount);
        Assert.Equal(1, waiting.MatchedTransactionCount);

        var page = await RunAsync(s => s.GetQueueAsync(
            seeded.VerifierAccountId,
            new VerificationQueueFilterDto { Keyword = seeded.Tag, Status = "submitted", Page = 2, PageSize = 3 }));
        Assert.Equal(4, page.TotalCount);
        Assert.Equal("chờ dữ liệu", Assert.Single(page.Items).WorkTitle);

        var past = await RunAsync(s => s.GetQueueAsync(
            seeded.VerifierAccountId,
            new VerificationQueueFilterDto { Keyword = seeded.Tag, Page = int.MaxValue }));
        Assert.Empty(past.Items);
        Assert.Equal(4, past.TotalCount);
    }
}
