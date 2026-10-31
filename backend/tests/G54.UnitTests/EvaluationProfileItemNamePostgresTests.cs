using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.EvaluationProfile;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// Runs the sibling group-name uniqueness on migrated PostgreSQL: the unique indexes and the clean-up of names that
/// were already duplicated only exist there. Skips when no Postgres is available (see <see cref="TestPostgres"/>).
/// </summary>
public sealed class EvaluationProfileItemNamePostgresTests(DemoSeedDatabaseFixture fixture) : IClassFixture<DemoSeedDatabaseFixture>
{
    private const string PreviousMigration = "20261029042206_RestructureEvaluationProfileItemTree";
    private const string Migration = "20261030174646_AddEvaluationProfileItemGroupNameUniqueIndex";

    private static int _sequence;

    private async Task<T> RunAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = fixture.Provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static EvaluationProfileItem Group(Guid profileId, Guid? parentId, string name, int order = 1) => new()
    {
        Id = Guid.NewGuid(),
        EvaluationProfileId = profileId,
        ParentItemId = parentId,
        Name = name,
        WeightPercent = 50,
        DisplayOrder = order,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>A DRAFT profile of its own, so the weight-sum triggers (ACTIVE only) do not apply.</summary>
    private static async Task<Guid> AddProfileAsync(AppDbContext db)
    {
        var number = Interlocked.Increment(ref _sequence);
        var now = DateTimeOffset.UtcNow;
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = $"NAME-{Guid.NewGuid():N}"[..20],
            Title = "Position",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = position.Id,
            Code = $"NAME-{number}-{Guid.NewGuid():N}"[..20],
            Name = "Profile",
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = EvaluationProfileStatus.Draft,
            UpdatedAt = now,
        };
        db.JobPositions.Add(position);
        db.EvaluationProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task<DbUpdateException> SaveExpectingUniqueViolationAsync(AppDbContext db)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);
        return ex;
    }

    [PostgresFact]
    public async Task TwoRootGroupsWithTheSameNameIgnoringCase_AreRejected()
    {
        var profileId = await RunAsync(AddProfileAsync);

        await RunAsync(async db =>
        {
            db.EvaluationProfileItems.Add(Group(profileId, null, "Business"));
            await db.SaveChangesAsync();
            return 0;
        });

        await RunAsync(async db =>
        {
            db.EvaluationProfileItems.Add(Group(profileId, null, "BUSINESS", 2));
            await SaveExpectingUniqueViolationAsync(db);
            return 0;
        });
    }

    [PostgresFact]
    public async Task TwoGroupsWithTheSameNameUnderOneParent_AreRejected()
    {
        var profileId = await RunAsync(AddProfileAsync);
        var parent = Group(profileId, null, "Parent");
        await RunAsync(async db =>
        {
            db.EvaluationProfileItems.AddRange(parent, Group(profileId, parent.Id, "Sub"));
            await db.SaveChangesAsync();
            return 0;
        });

        await RunAsync(async db =>
        {
            db.EvaluationProfileItems.Add(Group(profileId, parent.Id, "sub", 2));
            await SaveExpectingUniqueViolationAsync(db);
            return 0;
        });
    }

    [PostgresFact]
    public async Task TheSameNameUnderDifferentParentsOrInDifferentProfiles_IsAccepted()
    {
        var first = await RunAsync(AddProfileAsync);
        var second = await RunAsync(AddProfileAsync);
        var parentA = Group(first, null, "A");
        var parentB = Group(first, null, "B", 2);

        await RunAsync(async db =>
        {
            db.EvaluationProfileItems.AddRange(
                parentA,
                parentB,
                Group(first, parentA.Id, "Shared"),
                Group(first, parentB.Id, "Shared"),
                Group(second, null, "A"));
            await db.SaveChangesAsync();
            return 0;
        });

        var shared = await RunAsync(db => db.EvaluationProfileItems.CountAsync(i => i.EvaluationProfileId == first && i.Name == "Shared"));
        Assert.Equal(2, shared);
    }

    [PostgresFact]
    public async Task TwoConcurrentRequestsAddingTheSameGroup_OneSucceedsAndTheOtherIsAConflict()
    {
        var profileId = await RunAsync(AddProfileAsync);

        async Task<Exception?> AddAsync()
        {
            await using var scope = fixture.Provider!.CreateAsyncScope();
            var service = new EvaluationProfileService(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                NullLogger<EvaluationProfileService>.Instance);
            try
            {
                await service.AddItemAsync(profileId, new AddEvaluationProfileItemDto { Name = "Concurrent", WeightPercent = 50, DisplayOrder = 1 });
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(AddAsync(), AddAsync());

        Assert.Single(results, r => r is null);
        Assert.IsType<ConflictException>(results.Single(r => r is not null));
        var stored = await RunAsync(db => db.EvaluationProfileItems.CountAsync(i => i.EvaluationProfileId == profileId));
        Assert.Equal(1, stored);
    }

    [PostgresFact]
    public async Task MigratingOver_ExistingDuplicates_KeepsEveryRowAndRenamesAllButTheFirst()
    {
        var profileId = await RunAsync(AddProfileAsync);
        var parent = Group(profileId, null, "Parent");
        var first = Group(profileId, null, "Quality", 2);
        var rootDuplicate = Group(profileId, null, "QUALITY", 3);
        var child = Group(profileId, parent.Id, "Sub");
        var childDuplicate = Group(profileId, parent.Id, "SUB", 2);

        await RunAsync(async db =>
        {
            // Back to the schema without the indexes, so the duplicates can exist as they did before.
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            try
            {
                db.EvaluationProfileItems.AddRange(parent, first, rootDuplicate, child, childDuplicate);
                await db.SaveChangesAsync();
            }
            finally
            {
                await db.GetService<IMigrator>().MigrateAsync(Migration);
            }

            return 0;
        });

        var names = await RunAsync(async db => await db.EvaluationProfileItems
            .AsNoTracking()
            .Where(i => i.EvaluationProfileId == profileId)
            .ToDictionaryAsync(i => i.Id, i => i.Name));

        Assert.Equal(5, names.Count);
        Assert.Equal("Quality", names[first.Id]);
        Assert.Equal($"QUALITY [{rootDuplicate.Id.ToString()[..8]}]", names[rootDuplicate.Id]);
        Assert.Equal("Sub", names[child.Id]);
        Assert.Equal($"SUB [{childDuplicate.Id.ToString()[..8]}]", names[childDuplicate.Id]);
        Assert.Equal(names.Count, names.Values.Select(n => n!.ToUpperInvariant()).Distinct().Count());
    }

    [PostgresFact]
    public async Task RollingBackThisMigration_RemovesTheIndexesAndAllowsDuplicatesAgain()
    {
        var profileId = await RunAsync(AddProfileAsync);

        await RunAsync(async db =>
        {
            // The database is at the latest migration here, so this runs the Down of the migration under test.
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            try
            {
                db.EvaluationProfileItems.AddRange(Group(profileId, null, "Same"), Group(profileId, null, "SAME", 2));
                await db.SaveChangesAsync();

                var stored = await db.EvaluationProfileItems.CountAsync(i => i.EvaluationProfileId == profileId);
                Assert.Equal(2, stored);
            }
            finally
            {
                // Leave the database at the latest migration for the tests that follow.
                await db.GetService<IMigrator>().MigrateAsync(Migration);
            }

            return 0;
        });
    }
}
