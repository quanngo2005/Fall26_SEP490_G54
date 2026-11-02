using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// Period lock against the real schema: the guard must reject a locked period before anything is written, and the database
/// triggers, foreign keys and the serializable verification transaction must stay consistent afterwards.
/// </summary>
public sealed class PeriodLockPostgresTests : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private string _database = null!;
    private ServiceProvider _provider = null!;
    private Guid _adminAccount;
    private Guid _checkerAccount;
    private Guid _branchId;
    private Guid _deptId;
    private Guid _makerId;
    private Guid _kpiId;
    private Guid _yearId;
    private Guid _q1Id;
    private Guid _monthPeriodId;
    private Guid _workTypeId;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_lock_{Guid.NewGuid():N}";
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
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPermissionService, AllPermissions>();
        services.AddScoped<ITargetAssignmentService, TargetAssignmentService>();
        services.AddScoped<ITargetAllocationService, TargetAllocationService>();
        services.AddScoped(provider => new VerificationService(
            provider.GetRequiredService<AppDbContext>(),
            NullLogger<VerificationService>.Instance,
            TimeProvider.System,
            provider.GetRequiredService<IPermissionService>()));
        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
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

    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var branch = Department("LK-B", DepartmentLevelType.Branch, null, now);
        var dept = Department("LK-D", DepartmentLevelType.Department, branch.Id, now);
        db.Departments.AddRange(branch, dept);
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "LK-J", Title = "Job", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        db.JobPositions.Add(job);
        await db.SaveChangesAsync();

        var admin = Employee("LK-ADM", branch.Id, job.Id, now);
        var maker = Employee("LK-MAKER", dept.Id, job.Id, now);
        var checker = Employee("LK-CHECKER", dept.Id, job.Id, now);
        db.Employees.AddRange(admin, maker, checker);
        await db.SaveChangesAsync();
        var adminAccount = Account(admin, "lk-adm", now);
        var checkerAccount = Account(checker, "lk-checker", now);
        db.Accounts.AddRange(adminAccount, checkerAccount);

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "LK-G", Name = "G", Status = KpiStatus.Active, UpdatedAt = now };
        db.KpiGroups.Add(group);
        await db.SaveChangesAsync();
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "LK-SUM",
            Name = "Sum",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "LK-WT", Name = "WT", Status = RecordStatus.Active, UpdatedAt = now };
        db.KpiDefinitions.Add(kpi);
        db.WorkTypes.Add(workType);
        await db.SaveChangesAsync();
        db.WorkTypeKpis.Add(new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = kpi.Id,
            IsRequired = true,
            EffectiveFrom = Today.AddDays(-365),
            UpdatedAt = now,
        });

        // The schema enforces YEAR > HALF_YEAR > QUARTER > MONTH. All periods start OPEN; the tests flip individual ones.
        var year = Period("2026", PeriodType.Year, null, new(2026, 1, 1), new(2026, 12, 31), now);
        var h1 = Period("2026-H1", PeriodType.HalfYear, year.Id, new(2026, 1, 1), new(2026, 6, 30), now);
        var q1 = Period("2026-Q1", PeriodType.Quarter, h1.Id, new(2026, 1, 1), new(2026, 3, 31), now);
        var jan = Period("2026-01", PeriodType.Month, q1.Id, new(2026, 1, 1), new(2026, 1, 31), now);
        db.EvaluationPeriods.Add(year);
        await db.SaveChangesAsync();
        db.EvaluationPeriods.Add(h1);
        await db.SaveChangesAsync();
        db.EvaluationPeriods.Add(q1);
        await db.SaveChangesAsync();
        db.EvaluationPeriods.Add(jan);
        await db.SaveChangesAsync();

        (_adminAccount, _checkerAccount, _branchId, _deptId, _makerId, _kpiId, _yearId, _q1Id, _monthPeriodId, _workTypeId) =
            (adminAccount.Id, checkerAccount.Id, branch.Id, dept.Id, maker.Id, kpi.Id, year.Id, q1.Id, jan.Id, workType.Id);
    }

    private static Department Department(string code, DepartmentLevelType level, Guid? parent, DateTimeOffset now) => new()
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

    private static Employee Employee(string code, Guid departmentId, Guid jobId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = code,
        Email = $"{code.ToLowerInvariant()}@x.local",
        DepartmentId = departmentId,
        JobPositionId = jobId,
        Status = EmployeeStatus.Active,
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private static Account Account(Employee employee, string username, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employee.Id,
        Username = username,
        PasswordHash = "h",
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private static EvaluationPeriod Period(string code, PeriodType type, Guid? parent, DateOnly start, DateOnly end, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        PeriodType = type,
        ParentPeriodId = parent,
        StartDate = start,
        EndDate = end,
        Status = PeriodStatus.Open,
        UpdatedAt = now,
    };

    private async Task SetStatusAsync(Guid periodId, PeriodStatus status)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.EvaluationPeriods.SingleAsync(p => p.Id == periodId)).Status = status;
        await db.SaveChangesAsync();
    }

    private async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<int> CountAsync(Func<AppDbContext, IQueryable<object>> query)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>()).CountAsync();
    }

    // ----- target-assignments: create / allocations / period-allocations ------------------------------------

    [PostgresFact]
    public async Task CreateRoot_InALockedYear_Throws409AndStoresNothing_ThenSucceedsAfterReopening()
    {
        await SetStatusAsync(_yearId, PeriodStatus.Closed);
        var dto = new CreateTargetAssignmentDto { KpiDefinitionId = _kpiId, EvaluationPeriodId = _yearId, DepartmentId = _branchId, TargetValue = 1000 };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            RunAsync(sp => sp.GetRequiredService<ITargetAssignmentService>().CreateRootAsync(_adminAccount, dto)));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(0, await CountAsync(db => db.TargetAssignments));

        await SetStatusAsync(_yearId, PeriodStatus.Open);
        var created = await RunAsync(sp => sp.GetRequiredService<ITargetAssignmentService>().CreateRootAsync(_adminAccount, dto));
        Assert.Equal("ACTIVE", created.Status);
    }

    [PostgresFact]
    public async Task Allocate_AndPeriodAllocate_WhenTheirPeriodIsLocked_Throw409AndAddNoRows()
    {
        var root = await RunAsync(sp => sp.GetRequiredService<ITargetAssignmentService>().CreateRootAsync(
            _adminAccount,
            new CreateTargetAssignmentDto { KpiDefinitionId = _kpiId, EvaluationPeriodId = _yearId, DepartmentId = _branchId, TargetValue = 1000 }));
        var split = new AllocateTargetDto { Allocations = [new() { DepartmentId = _deptId, TargetValue = 1000 }] };
        var byPeriod = new AllocateTargetByPeriodDto { Allocations = [new() { EvaluationPeriodId = _q1Id, TargetValue = 1000 }] };

        // A locked target period blocks period-allocations even though the parent year is still open.
        await SetStatusAsync(_q1Id, PeriodStatus.Locked);
        var ex1 = await Assert.ThrowsAsync<ConflictException>(() =>
            RunAsync(sp => sp.GetRequiredService<ITargetAssignmentService>().AllocateByPeriodAsync(_adminAccount, root.Id, byPeriod)));
        Assert.Equal(ErrorCodes.PeriodLocked, ex1.ErrorCode);

        // A locked parent period blocks both splits.
        await SetStatusAsync(_q1Id, PeriodStatus.Open);
        await SetStatusAsync(_yearId, PeriodStatus.Locked);
        var ex2 = await Assert.ThrowsAsync<ConflictException>(() =>
            RunAsync(sp => sp.GetRequiredService<ITargetAssignmentService>().AllocateAsync(_adminAccount, root.Id, split)));
        var ex3 = await Assert.ThrowsAsync<ConflictException>(() =>
            RunAsync(sp => sp.GetRequiredService<ITargetAssignmentService>().AllocateByPeriodAsync(_adminAccount, root.Id, byPeriod)));
        Assert.Equal(ErrorCodes.PeriodLocked, ex2.ErrorCode);
        Assert.Equal(ErrorCodes.PeriodLocked, ex3.ErrorCode);

        Assert.Equal(1, await CountAsync(db => db.TargetAssignments));
    }

    // ----- confirm ---------------------------------------------------------------------------------------

    private async Task<Guid> PrepareDraftDepartmentYearAsync()
    {
        var branchYear = await RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var row = new TargetAssignment
            {
                Id = Guid.NewGuid(),
                DepartmentId = _branchId,
                KpiDefinitionId = _kpiId,
                EvaluationPeriodId = _yearId,
                TargetValue = 1000,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
            };
            db.TargetAssignments.Add(row);
            await db.SaveChangesAsync();
            return row.Id;
        });
        await RunAsync(sp => sp.GetRequiredService<ITargetAllocationService>()
            .AllocateDepartmentsAsync(branchYear, new([new(_deptId, _kpiId, 1000m)], null), _adminAccount));
        var deptYear = await RunAsync(async sp =>
            (await sp.GetRequiredService<AppDbContext>().TargetAssignments.SingleAsync(t => t.ParentAssignmentId == branchYear)).Id);
        await RunAsync(sp => sp.GetRequiredService<ITargetAllocationService>()
            .AllocateQuartersAsync(deptYear, new([new(_q1Id, _kpiId, 1000m)], null), _adminAccount));
        return deptYear;
    }

    [PostgresFact]
    public async Task Confirm_WhenAGeneratedMonthIsLocked_Throws409AndLeavesTheDraftTreeUntouched_ThenConfirmsWhenOpen()
    {
        var deptYear = await PrepareDraftDepartmentYearAsync();
        await SetStatusAsync(_monthPeriodId, PeriodStatus.Closed);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            RunAsync(sp => sp.GetRequiredService<ITargetAllocationService>().ConfirmAsync(deptYear, _adminAccount)));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Equal(0, await CountAsync(db => db.TargetAssignments.Where(t => t.EvaluationPeriod.PeriodType == PeriodType.Month)));
        Assert.Equal(0, await CountAsync(db => db.TargetAssignments.Where(t => t.Status == TargetAssignmentStatus.Active)));

        await SetStatusAsync(_monthPeriodId, PeriodStatus.Open);
        await RunAsync(sp => sp.GetRequiredService<ITargetAllocationService>().ConfirmAsync(deptYear, _adminAccount));
        Assert.Equal(1, await CountAsync(db => db.TargetAssignments.Where(t => t.EvaluationPeriod.PeriodType == PeriodType.Month)));
    }

    [PostgresFact]
    public async Task Confirm_WhenTheYearIsLockedAfterAllocating_Throws409()
    {
        var deptYear = await PrepareDraftDepartmentYearAsync();
        await SetStatusAsync(_yearId, PeriodStatus.Locked);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            RunAsync(sp => sp.GetRequiredService<ITargetAllocationService>().ConfirmAsync(deptYear, _adminAccount)));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
    }

    // ----- verification: approved result into a locked period ----------------------------------------------

    private async Task<Guid> SeedSubmittedWorkAsync(PeriodStatus periodStatus)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        // A month that covers today, under its own YEAR > HALF_YEAR > QUARTER chain (the schema rejects orphan periods).
        // The parents stay OPEN so only the month's status decides the outcome.
        var tag = Guid.NewGuid().ToString("N")[..8];
        var parentStart = Today.AddDays(-30);
        var parentEnd = Today.AddDays(30);
        var yearOfToday = Period($"LKY-{tag}", PeriodType.Year, null, parentStart, parentEnd, now);
        db.EvaluationPeriods.Add(yearOfToday);
        await db.SaveChangesAsync();
        var half = Period($"LKH-{tag}", PeriodType.HalfYear, yearOfToday.Id, parentStart, parentEnd, now);
        db.EvaluationPeriods.Add(half);
        await db.SaveChangesAsync();
        var quarter = Period($"LKQ-{tag}", PeriodType.Quarter, half.Id, parentStart, parentEnd, now);
        db.EvaluationPeriods.Add(quarter);
        await db.SaveChangesAsync();
        var period = Period($"LKM-{tag}", PeriodType.Month, quarter.Id, Today.AddDays(-5), Today.AddDays(5), now);
        period.Status = periodStatus;
        db.EvaluationPeriods.Add(period);
        await db.SaveChangesAsync();
        db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = _kpiId,
            EmployeeId = _makerId,
            EvaluationPeriodId = period.Id,
            Version = 1,
            TargetValue = 100,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            ApprovedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"LK-W-{Guid.NewGuid():N}"[..14],
            Title = "Work",
            WorkTypeId = _workTypeId,
            AssigneeEmployeeId = _makerId,
            CreatedBy = _makerId,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = now,
        };
        db.Works.Add(work);
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = _makerId,
            SubmissionNo = 1,
            Status = SubmissionStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
        };
        db.ResultSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return submission.Id;
    }

    private Task<VerificationDecisionResultDto> ApproveAsync(Guid submissionId, decimal value) =>
        RunAsync(sp => sp.GetRequiredService<VerificationService>().SubmitDecisionAsync(
            submissionId,
            _checkerAccount,
            new SubmitVerificationDecisionDto { Decision = "APPROVED", VerifiedValue = value }));

    [PostgresFact]
    public async Task Approve_IntoALockedPeriod_Throws409_RollsBackTheSerializableTransaction_AndStaysApprovableAfterReopening()
    {
        var submissionId = await SeedSubmittedWorkAsync(PeriodStatus.Locked);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(submissionId, 25m));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Equal(0, await CountAsync(db => db.Verifications));
        Assert.Equal(0, await CountAsync(db => db.VerifiedWorkResults));
        Assert.Equal(0, await CountAsync(db => db.WorkResultItems));
        Assert.Equal(0, await CountAsync(db => db.ActualProgresses));
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(SubmissionStatus.Submitted, (await db.ResultSubmissions.SingleAsync()).Status);
            Assert.Equal(WorkStatus.Submitted, (await db.Works.SingleAsync()).Status);
        }

        // The rejection left no half-committed state behind: after the period reopens, the same submission is approvable.
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.EvaluationPeriods.SingleAsync(p => p.Status == PeriodStatus.Locked)).Status = PeriodStatus.Open;
            await db.SaveChangesAsync();
        }

        var approved = await ApproveAsync(submissionId, 25m);
        Assert.NotEqual(Guid.Empty, approved.VerificationId);
        Assert.Equal(1, await CountAsync(db => db.WorkResultItems));
        await using var verify = _provider.CreateAsyncScope();
        Assert.Equal(25m, (await verify.ServiceProvider.GetRequiredService<AppDbContext>().ActualProgresses.SingleAsync()).Value);
    }

    [PostgresFact]
    public async Task Approve_IntoAnOpenOrInReviewPeriod_StillWritesProgress()
    {
        var open = await SeedSubmittedWorkAsync(PeriodStatus.Open);
        await ApproveAsync(open, 10m);
        var inReview = await SeedSubmittedWorkAsync(PeriodStatus.InReview);
        await ApproveAsync(inReview, 5m);

        // Two different periods cover today; the one ending first wins, so both approvals land on the same open target row set.
        Assert.Equal(2, await CountAsync(db => db.WorkResultItems));
    }

    private sealed class AllPermissions : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([new("verification", "decide", "All")]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
