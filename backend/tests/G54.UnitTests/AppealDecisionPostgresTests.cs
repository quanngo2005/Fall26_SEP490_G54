using G54.BLL.Common;
using G54.BLL.Common.Constants;
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

    [PostgresFact]
    public async Task Accept_WhenWorkHasKpiAndVerifiedValueButNoActiveTarget_ThrowsConflictAndRollsBackCleanly()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D-RB1",
            Name = "Department",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var job = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "J-RB1",
            Title = "Job",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Departments.Add(department);
        db.JobPositions.Add(job);
        var maker = Employee("MAKER-RB1", department.Id, job.Id, now);
        var checker = Employee("CHECKER-RB1", department.Id, job.Id, now);
        db.Employees.AddRange(maker, checker);
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = checker.Id,
            Username = "checker-rb1",
            PasswordHash = "test-hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Accounts.Add(account);

        var kpiGroup = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "G-RB1",
            Name = "Group",
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            Code = "KPI-RB1",
            Name = "KPI Rollback Test 1",
            Unit = "Point",
            KpiGroupId = kpiGroup.Id,
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
        };
        var workType = new WorkType
        {
            Id = Guid.NewGuid(),
            Code = "WT-RB1",
            Name = "Work Type",
            Status = RecordStatus.Active,
        };
        var workTypeKpi = new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = kpi.Id,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        };
        db.KpiGroups.Add(kpiGroup);
        db.KpiDefinitions.Add(kpi);
        db.WorkTypes.Add(workType);
        db.WorkTypeKpis.Add(workTypeKpi);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-RB1",
            Title = "Work Rollback Test 1",
            AssigneeEmployeeId = maker.Id,
            WorkTypeId = workType.Id,
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
            Comment = G54.BLL.Services.ResultSubmissionService.BuildStoredComment(null, [new G54.BLL.Dtos.Submission.SubmissionMetricInputDto(kpi.Id, 10m, null)]),
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

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DecideAppealAsync(
            appeal.Id, account.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));

        Assert.Equal(ErrorCodes.KpiTargetNotAssigned, ex.ErrorCode);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict, ex.StatusCode);

        db.ChangeTracker.Clear();
        var appealInDb = await db.Appeals.SingleAsync(a => a.Id == appeal.Id);
        Assert.Equal(AppealStatus.Open, appealInDb.Status);
        Assert.Null(appealInDb.ResolvedBy);

        var submissionInDb = await db.ResultSubmissions.SingleAsync(s => s.Id == submission.Id);
        Assert.Equal(SubmissionStatus.Rejected, submissionInDb.Status);

        var workInDb = await db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(WorkStatus.Rejected, workInDb.Status);

        Assert.False(await db.Verifications.AnyAsync(v => v.ResultSubmissionId == submission.Id));
        Assert.False(await db.VerifiedWorkResults.AnyAsync());
        Assert.False(await db.WorkResultItems.AnyAsync(i => i.EmployeeId == maker.Id));
        Assert.False(await db.ActualProgresses.AnyAsync());
    }

    [PostgresFact]
    public async Task VerificationApprove_WhenWorkHasKpiAndVerifiedValueButNoActiveTarget_ThrowsConflictAndRollsBackCleanly()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D-RB2",
            Name = "Department",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var job = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "J-RB2",
            Title = "Job",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Departments.Add(department);
        db.JobPositions.Add(job);
        var maker = Employee("MAKER-RB2", department.Id, job.Id, now);
        var checker = Employee("CHECKER-RB2", department.Id, job.Id, now);
        db.Employees.AddRange(maker, checker);
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = checker.Id,
            Username = "checker-rb2",
            PasswordHash = "test-hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        db.Accounts.Add(account);

        var kpiGroup = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "G-RB2",
            Name = "Group",
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            Code = "KPI-RB2",
            Name = "KPI Rollback Test 2",
            Unit = "Point",
            KpiGroupId = kpiGroup.Id,
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
        };
        var workType = new WorkType
        {
            Id = Guid.NewGuid(),
            Code = "WT-RB2",
            Name = "Work Type",
            Status = RecordStatus.Active,
        };
        var workTypeKpi = new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = kpi.Id,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        };
        db.KpiGroups.Add(kpiGroup);
        db.KpiDefinitions.Add(kpi);
        db.WorkTypes.Add(workType);
        db.WorkTypeKpis.Add(workTypeKpi);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-RB2",
            Title = "Work Rollback Test 2",
            AssigneeEmployeeId = maker.Id,
            WorkTypeId = workType.Id,
            CreatedBy = maker.Id,
            Status = WorkStatus.Submitted,
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
            Status = SubmissionStatus.Submitted,
            Comment = G54.BLL.Services.ResultSubmissionService.BuildStoredComment(null, [new G54.BLL.Dtos.Submission.SubmissionMetricInputDto(kpi.Id, 10m, null)]),
            SubmittedAt = now,
            UpdatedAt = now,
        };
        db.ResultSubmissions.Add(submission);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new VerificationService(
            db,
            NullLogger<VerificationService>.Instance,
            TimeProvider.System,
            new AllPermissions());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitDecisionAsync(
            submission.Id, account.Id, new G54.BLL.Dtos.Verification.SubmitVerificationDecisionDto
            {
                Decision = "APPROVED",
                Feedback = "Phê duyệt nhưng thiếu target",
            }));

        Assert.Equal(ErrorCodes.KpiTargetNotAssigned, ex.ErrorCode);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict, ex.StatusCode);

        db.ChangeTracker.Clear();
        var submissionInDb = await db.ResultSubmissions.SingleAsync(s => s.Id == submission.Id);
        Assert.Equal(SubmissionStatus.Submitted, submissionInDb.Status);

        var workInDb = await db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(WorkStatus.Submitted, workInDb.Status);

        Assert.False(await db.Verifications.AnyAsync(v => v.ResultSubmissionId == submission.Id));
        Assert.False(await db.VerifiedWorkResults.AnyAsync());
        Assert.False(await db.WorkResultItems.AnyAsync(i => i.EmployeeId == maker.Id));
        Assert.False(await db.ActualProgresses.AnyAsync());
    }

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
