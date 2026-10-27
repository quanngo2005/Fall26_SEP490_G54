using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Appeal;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace G54.UnitTests;

/// <summary>Checks appeal decisions against real foreign keys and atomic PostgreSQL persistence.</summary>
public sealed class AppealDecisionPostgresTests : IAsyncLifetime
{
    private string _database = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_appeal_{Guid.NewGuid():N}";
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
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
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

    [PostgresFact]
    public Task Accept_PersistsApprovalAndResolverAtomically_AndCannotBeDecidedAgain() =>
        CheckDecisionAsync("ACCEPT", AppealStatus.Accepted, SubmissionStatus.Approved, WorkStatus.Verified);

    [PostgresFact]
    public Task Adjust_PersistsRevisionWithoutApproval() =>
        CheckDecisionAsync("ADJUST", AppealStatus.Accepted, SubmissionStatus.UnderReview, WorkStatus.InProgress);

    [PostgresFact]
    public Task Reject_PersistsRejectionWithoutVerification() =>
        CheckDecisionAsync("REJECT", AppealStatus.Rejected, SubmissionStatus.Rejected, WorkStatus.Rejected);

    private async Task CheckDecisionAsync(string decision, AppealStatus appealStatus, SubmissionStatus submissionStatus, WorkStatus workStatus)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D",
            Name = "Department",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var job = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "J",
            Title = "Job",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Departments.Add(department);
        db.JobPositions.Add(job);
        var maker = Employee("MAKER", department.Id, job.Id, now);
        var checker = Employee("CHECKER", department.Id, job.Id, now);
        db.Employees.AddRange(maker, checker);
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = checker.Id,
            Username = "checker",
            PasswordHash = "test-hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Accounts.Add(account);
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W",
            Title = "Work",
            AssigneeEmployeeId = maker.Id,
            CreatedBy = maker.Id,
            Status = WorkStatus.Rejected,
            Priority = WorkPriority.Medium,
            UpdatedAt = now,
        };
        db.Works.Add(work);
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = maker.Id,
            SubmissionNo = 1,
            Status = SubmissionStatus.Rejected,
            SubmittedAt = now,
            UpdatedAt = now,
        };
        db.ResultSubmissions.Add(submission);
        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = maker.Id,
            ResultSubmissionId = submission.Id,
            Reason = "Xem xét lại kết quả",
            Status = AppealStatus.Open,
            FiledAt = now,
            UpdatedAt = now,
        };
        db.Appeals.Add(appeal);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new AppealService(db, new AllPermissions(),
            new StorageService(new ConfigurationBuilder().Build(), NullLogger<StorageService>.Instance),
            NullLogger<AppealService>.Instance, TimeProvider.System, Options.Create(new SystemParametersOptions()));

        var result = await service.DecideAppealAsync(appeal.Id, account.Id,
            new DecideAppealRequestDto { Decision = decision, Note = "Ghi chú xử lý khiếu nại" });

        db.ChangeTracker.Clear();
        Assert.Equal(appealStatus, (await db.Appeals.SingleAsync()).Status);
        Assert.Equal(checker.Id, (await db.Appeals.SingleAsync()).ResolvedBy);
        Assert.Equal(submissionStatus, (await db.ResultSubmissions.SingleAsync()).Status);
        Assert.Equal(workStatus, (await db.Works.SingleAsync()).Status);
        if (decision == "ACCEPT")
        {
            Assert.Equal(result.VerificationId, (await db.VerifiedWorkResults.SingleAsync()).VerificationId);
        }
        else
        {
            Assert.Empty(await db.VerifiedWorkResults.ToListAsync());
        }

        if (decision != "REJECT")
        {
            Assert.Equal(decision == "ACCEPT" ? VerificationDecision.Approved : VerificationDecision.NeedMoreInfo,
                (await db.Verifications.SingleAsync()).Decision);
        }
        await Assert.ThrowsAsync<ConflictException>(() => service.DecideAppealAsync(
            appeal.Id, account.Id, new DecideAppealRequestDto { Decision = "REJECT", Note = "Không đủ cơ sở chấp nhận" }));
        Assert.Equal(decision == "REJECT" ? 0 : 1, await db.Verifications.CountAsync());
    }

    private static Employee Employee(string code, Guid departmentId, Guid jobId, DateTimeOffset now) => new()
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

    private sealed class AllPermissions : IPermissionService
    {
        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([new("verification", "decide", "All")]);

        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
