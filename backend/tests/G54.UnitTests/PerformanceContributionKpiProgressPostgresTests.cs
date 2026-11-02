using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// Runs the KPI-row uniqueness of <c>performance_contribution</c> on migrated PostgreSQL: the partial unique index and the
/// clean-up of rows that already share an evaluation and a progress row only exist there. Skips when no Postgres is
/// available (see <see cref="TestPostgres"/>).
/// </summary>
public sealed class PerformanceContributionKpiProgressPostgresTests(DemoSeedDatabaseFixture fixture) : IClassFixture<DemoSeedDatabaseFixture>
{
    /// <summary>The migration just before the KPI-row index (<c>AddPerformanceContributionKpiProgressUniqueIndex</c>).</summary>
    private const string PreviousMigration = "20261030174646_AddEvaluationProfileItemGroupNameUniqueIndex";

    private static int _sequence;

    private async Task<T> RunAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = fixture.Provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>An evaluation with one progress row, in a database of its own rows so tests do not collide.</summary>
    private sealed record Setup(Guid EvaluationId, Guid EmployeeId, Guid PeriodId, Guid ProgressId, Guid SecondProgressId);

    private static async Task<Setup> AddEvaluationAsync(AppDbContext db)
    {
        var number = Interlocked.Increment(ref _sequence);
        var now = DateTimeOffset.UtcNow;
        var code = $"PC{number}-{Guid.NewGuid():N}"[..20];

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = code, Title = code, Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code}@bpms.test".ToLowerInvariant(),
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = code, Name = code, Status = KpiStatus.Active, UpdatedAt = now };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = code,
            Name = code,
            Unit = "unit",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = position.Id,
            Code = code,
            Name = code,
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = EvaluationProfileStatus.Draft,
            UpdatedAt = now,
        };
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Open,
            UpdatedAt = now,
        };
        var target = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = employee.Id,
            EvaluationPeriodId = period.Id,
            Version = 1,
            TargetValue = 100m,
            Status = TargetAssignmentStatus.Active,
            ApprovedAt = now,
            CreatedAt = now.AddMinutes(-1),
            UpdatedAt = now,
        };
        ActualProgress Progress(int day) => new()
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = target.Id,
            ProgressDate = new DateOnly(2026, 10, day),
            Value = 10m,
            Status = ActualProgressStatus.Valid,
            UpdatedAt = now,
        };
        var first = Progress(10);
        var second = Progress(11);
        var evaluation = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = profile.Id,
            Status = PerformanceEvaluationStatus.Draft,
            UpdatedAt = now,
        };

        db.Departments.Add(department);
        db.JobPositions.Add(position);
        db.Employees.Add(employee);
        db.KpiGroups.Add(group);
        db.KpiDefinitions.Add(kpi);
        db.EvaluationProfiles.Add(profile);
        db.EvaluationPeriods.Add(period);
        db.TargetAssignments.Add(target);
        db.ActualProgresses.AddRange(first, second);
        db.PerformanceEvaluations.Add(evaluation);
        await db.SaveChangesAsync();
        return new Setup(evaluation.Id, employee.Id, period.Id, first.Id, second.Id);
    }

    private static PerformanceContribution KpiRow(Setup s, Guid? progressId, DateTimeOffset updatedAt, Guid? riskEventId = null) => new()
    {
        Id = Guid.NewGuid(),
        PerformanceEvaluationId = s.EvaluationId,
        EmployeeId = s.EmployeeId,
        EvaluationPeriodId = s.PeriodId,
        ActualProgressId = progressId,
        RiskEventId = riskEventId,
        ComponentScore = 1m,
        UpdatedAt = updatedAt,
    };

    private static async Task<DbUpdateException> SaveExpectingUniqueViolationAsync(AppDbContext db)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);
        return ex;
    }

    [PostgresFact]
    public async Task TwoKpiRowsOfOneEvaluationOnTheSameProgressRow_AreRejected()
    {
        var s = await RunAsync(AddEvaluationAsync);
        await RunAsync(async db =>
        {
            db.PerformanceContributions.Add(KpiRow(s, s.ProgressId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });

        await RunAsync(async db =>
        {
            db.PerformanceContributions.Add(KpiRow(s, s.ProgressId, DateTimeOffset.UtcNow));
            await SaveExpectingUniqueViolationAsync(db);
            return 0;
        });
    }

    [PostgresFact]
    public async Task KpiRowsOnDifferentProgressRowsOrOfDifferentEvaluations_AreAccepted()
    {
        var first = await RunAsync(AddEvaluationAsync);
        var second = await RunAsync(AddEvaluationAsync);

        await RunAsync(async db =>
        {
            db.PerformanceContributions.AddRange(
                KpiRow(first, first.ProgressId, DateTimeOffset.UtcNow),
                KpiRow(first, first.SecondProgressId, DateTimeOffset.UtcNow),
                KpiRow(second, second.ProgressId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });

        Assert.Equal(2, await RunAsync(db => db.PerformanceContributions.CountAsync(c => c.PerformanceEvaluationId == first.EvaluationId)));
    }

    [PostgresFact]
    public async Task RowsWithoutAProgressRow_AreNotLimited()
    {
        var s = await RunAsync(AddEvaluationAsync);

        await RunAsync(async db =>
        {
            db.PerformanceContributions.AddRange(
                KpiRow(s, null, DateTimeOffset.UtcNow),
                KpiRow(s, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });

        Assert.Equal(2, await RunAsync(db => db.PerformanceContributions.CountAsync(c => c.PerformanceEvaluationId == s.EvaluationId)));
    }

    [PostgresFact]
    public async Task TwoConcurrentSavesOfTheSameKpiRow_OneSucceedsAndTheOtherIsAUniqueViolation()
    {
        var s = await RunAsync(AddEvaluationAsync);

        async Task<Exception?> InsertAsync()
        {
            await using var scope = fixture.Provider!.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PerformanceContributions.Add(KpiRow(s, s.ProgressId, DateTimeOffset.UtcNow));
            try
            {
                await db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(InsertAsync(), InsertAsync());

        Assert.Single(results, r => r is null);
        var failure = Assert.IsType<DbUpdateException>(results.Single(r => r is not null));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)failure.InnerException!).SqlState);
        Assert.Equal(1, await RunAsync(db => db.PerformanceContributions.CountAsync(c => c.PerformanceEvaluationId == s.EvaluationId)));
    }

    [PostgresFact]
    public async Task MigratingOver_ExistingDuplicates_KeepsTheMostRecentlyUpdatedRowOfEachPairAndNothingElse()
    {
        var s = await RunAsync(AddEvaluationAsync);
        var old = KpiRow(s, s.ProgressId, DateTimeOffset.UtcNow.AddHours(-2));
        var latest = KpiRow(s, s.ProgressId, DateTimeOffset.UtcNow);
        var other = KpiRow(s, s.SecondProgressId, DateTimeOffset.UtcNow.AddHours(-1));
        var noProgress = KpiRow(s, null, DateTimeOffset.UtcNow);

        await RunAsync(async db =>
        {
            // Back to the schema without the index, so the duplicates can exist as they did before.
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            try
            {
                db.PerformanceContributions.AddRange(old, latest, other, noProgress);
                await db.SaveChangesAsync();
            }
            finally
            {
                await db.GetService<IMigrator>().MigrateAsync();
            }

            return 0;
        });

        var ids = await RunAsync(async db => await db.PerformanceContributions
            .AsNoTracking()
            .Where(c => c.PerformanceEvaluationId == s.EvaluationId)
            .Select(c => c.Id)
            .ToListAsync());

        Assert.Equal(3, ids.Count);
        Assert.DoesNotContain(old.Id, ids);
        Assert.Contains(latest.Id, ids);
        Assert.Contains(other.Id, ids);
        Assert.Contains(noProgress.Id, ids);
    }

    [PostgresFact]
    public async Task RollingBackThisMigration_RemovesTheIndexAndAllowsDuplicatesAgain()
    {
        var s = await RunAsync(AddEvaluationAsync);

        await RunAsync(async db =>
        {
            // The database is at the latest migration here, so this runs the Down of the migration under test
            // (and of the ones after it).
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            try
            {
                db.PerformanceContributions.AddRange(
                    KpiRow(s, s.ProgressId, DateTimeOffset.UtcNow),
                    KpiRow(s, s.ProgressId, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();

                Assert.Equal(2, await db.PerformanceContributions.CountAsync(c => c.PerformanceEvaluationId == s.EvaluationId));
            }
            finally
            {
                // Leave the database at the latest migration for the tests that follow.
                await db.GetService<IMigrator>().MigrateAsync();
            }

            return 0;
        });
    }
}
