using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Quality;
using G54.BLL.Dtos.Submission;
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
/// QA rework against the real schema: invalidating a verified result must take its RAW amount back out of the day's
/// <c>actual_progress</c> row (the multiplier is applied only when scoring), and results with different multipliers on the same
/// day must stay consistent (migration <c>FixInvalidateProgressUsesRawValue</c>).
/// </summary>
public sealed class QualityReworkPostgresTests : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private string _database = null!;
    private ServiceProvider _provider = null!;
    private Guid _makerId;
    private Guid _checkerAccount;
    private Guid _qaAccount;
    private Guid _kpiId;
    private Guid _goldTypeId;
    private Guid _classicTypeId;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_qarework_{Guid.NewGuid():N}";
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
        services.AddSingleton(TimeProvider.System);
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

    private static Employee NewEmployee(string code, Guid departmentId, Guid jobId, DateTimeOffset now) => new()
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

    private static Account NewAccount(Employee employee, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employee.Id,
        Username = employee.EmployeeCode.ToLowerInvariant(),
        PasswordHash = "h",
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private static EvaluationPeriod NewPeriod(string code, PeriodType type, Guid? parent, DateOnly start, DateOnly end, DateTimeOffset now) => new()
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

    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "QR-D",
            Name = "QR-D",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "QR-J", Title = "Job", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        db.Departments.Add(dept);
        db.JobPositions.Add(job);
        await db.SaveChangesAsync();

        var maker = NewEmployee("QR-MAKER", dept.Id, job.Id, now);
        var checker = NewEmployee("QR-CHECKER", dept.Id, job.Id, now);
        var qa = NewEmployee("QR-QA", dept.Id, job.Id, now);
        db.Employees.AddRange(maker, checker, qa);
        await db.SaveChangesAsync();
        var checkerAccount = NewAccount(checker, now);
        var qaAccount = NewAccount(qa, now);
        db.Accounts.AddRange(checkerAccount, qaAccount);

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "QR-G", Name = "G", Status = KpiStatus.Active, UpdatedAt = now };
        db.KpiGroups.Add(group);
        await db.SaveChangesAsync();
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "QR-SUM",
            Name = "Sum",
            Unit = "card",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var gold = new WorkType { Id = Guid.NewGuid(), Code = "QR-GOLD", Name = "Gold card", Status = RecordStatus.Active, UpdatedAt = now };
        var classic = new WorkType { Id = Guid.NewGuid(), Code = "QR-CLASSIC", Name = "Classic card", Status = RecordStatus.Active, UpdatedAt = now };
        db.KpiDefinitions.Add(kpi);
        db.WorkTypes.AddRange(gold, classic);
        await db.SaveChangesAsync();
        foreach (var (type, multiplier) in new[] { (gold, 1.5m), (classic, 1m) })
        {
            db.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = type.Id,
                KpiDefinitionId = kpi.Id,
                IsRequired = true,
                Multiplier = multiplier,
                EffectiveFrom = Today.AddDays(-365),
                UpdatedAt = now,
            });
        }

        var year = NewPeriod("QR-Y", PeriodType.Year, null, Today.AddDays(-30), Today.AddDays(30), now);
        db.EvaluationPeriods.Add(year);
        await db.SaveChangesAsync();
        var half = NewPeriod("QR-H", PeriodType.HalfYear, year.Id, Today.AddDays(-30), Today.AddDays(30), now);
        db.EvaluationPeriods.Add(half);
        await db.SaveChangesAsync();
        var quarter = NewPeriod("QR-Q", PeriodType.Quarter, half.Id, Today.AddDays(-30), Today.AddDays(30), now);
        db.EvaluationPeriods.Add(quarter);
        await db.SaveChangesAsync();
        var month = NewPeriod("QR-M", PeriodType.Month, quarter.Id, Today.AddDays(-5), Today.AddDays(5), now);
        db.EvaluationPeriods.Add(month);
        await db.SaveChangesAsync();
        db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = maker.Id,
            EvaluationPeriodId = month.Id,
            Version = 1,
            TargetValue = 100,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            ApprovedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        _makerId = maker.Id;
        _checkerAccount = checkerAccount.Id;
        _qaAccount = qaAccount.Id;
        _kpiId = kpi.Id;
        _goldTypeId = gold.Id;
        _classicTypeId = classic.Id;
    }

    /// <summary>Submits and approves one work of the given type; returns its submission id.</summary>
    private async Task<Guid> ApproveWorkAsync(Guid workTypeId, decimal value)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"QR-W-{Guid.NewGuid():N}"[..14],
            Title = "Work",
            WorkTypeId = workTypeId,
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
            Comment = ResultSubmissionService.BuildStoredComment(null, [new SubmissionMetricInputDto(_kpiId, value, null)]),
            Status = SubmissionStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
        };
        db.ResultSubmissions.Add(submission);
        await db.SaveChangesAsync();

        var verification = new VerificationService(
            db,
            NullLogger<VerificationService>.Instance,
            TimeProvider.System,
            new FakePermissionService(null, new UserPermissionDto("verification", "decide", "All", "VERIFICATION_DECIDE")));
        await verification.SubmitDecisionAsync(submission.Id, _checkerAccount, new SubmitVerificationDecisionDto { Decision = "APPROVED" });
        return submission.Id;
    }

    private async Task ReworkAsync(Guid submissionId)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var qa = new QualityAssuranceService(db, TimeProvider.System, new RecordingNotifier(), NullLogger<QualityAssuranceService>.Instance);
        var flag = await qa.CreateFlagAsync(submissionId, _qaAccount, new CreateQualityFlagDto("Số liệu không khớp chứng từ gốc.", null));
        await qa.RequestReworkAsync(flag.Id, _qaAccount, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ."));
    }

    private async Task<ActualProgress> ProgressAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ActualProgresses.AsNoTracking().SingleAsync();
    }

    [PostgresFact]
    public async Task Rework_TakesTheRawAmountOutOfTheDay_WhateverTheMultiplierOfEachResult()
    {
        var goldSubmission = await ApproveWorkAsync(_goldTypeId, 10m);      // x1.5: value 15, raw 10
        var classicSubmission = await ApproveWorkAsync(_classicTypeId, 20m); // x1:   value 20, raw 20
        var both = await ProgressAsync();
        Assert.Equal(30m, both.Value);
        Assert.Equal(ActualProgressStatus.Valid, both.Status);

        await ReworkAsync(goldSubmission);

        // 30 - 10 raw = 20. Subtracting the multiplied 15 would have left 15.
        var afterGold = await ProgressAsync();
        Assert.Equal(20m, afterGold.Value);
        Assert.Equal(ActualProgressStatus.Valid, afterGold.Status);

        await ReworkAsync(classicSubmission);

        var empty = await ProgressAsync();
        Assert.Equal(0m, empty.Value);
        Assert.Equal(ActualProgressStatus.Invalid, empty.Status);
    }

    [PostgresFact]
    public async Task Rework_OfTheOnlyResult_InvalidatesTheDay_AndANewApprovalBringsItBackToLife()
    {
        var first = await ApproveWorkAsync(_goldTypeId, 10m);
        await ReworkAsync(first);
        Assert.Equal(ActualProgressStatus.Invalid, (await ProgressAsync()).Status);

        await ApproveWorkAsync(_classicTypeId, 7m);

        var revived = await ProgressAsync();
        Assert.Equal(7m, revived.Value);
        Assert.Equal(ActualProgressStatus.Valid, revived.Status);
    }

    [PostgresFact]
    public async Task Rework_LeavesTheResultInvalidAndTheWorkInProgress()
    {
        var first = await ApproveWorkAsync(_goldTypeId, 10m);
        await ReworkAsync(first);

        // The reworked result no longer counts for scoring and the work is open again.
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(VerifiedResultStatus.Invalid, (await db.VerifiedWorkResults.SingleAsync()).Status);
        Assert.Equal(WorkStatus.InProgress, (await db.Works.SingleAsync()).Status);
    }
}
