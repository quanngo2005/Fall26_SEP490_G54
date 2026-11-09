using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace G54.UnitTests;

/// <summary>
/// Runs <see cref="AuditLogRepository"/> on migrated PostgreSQL: <c>EF.Functions.ILike</c> only works there.
/// Skips when no Postgres is available (see <see cref="TestPostgres"/>).
/// </summary>
public sealed class AuditLogRepositoryPostgresTests(DemoSeedDatabaseFixture fixture) : IClassFixture<DemoSeedDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);

    private async Task<T> RunAsync<T>(Func<AuditLogRepository, AppDbContext, Task<T>> action)
    {
        await using var scope = fixture.Provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(new AuditLogRepository(db), db);
    }

    [PostgresFact]
    public async Task GetPagedAsync_FiltersSortsAndPaginates()
    {
        var entityName = $"TestEntity-{Guid.NewGuid():N}";
        var actorId = Guid.NewGuid();

        await RunAsync(async (repo, db) =>
        {
            db.AuditLogs.AddRange(
                new AuditLog { Id = Guid.NewGuid(), EntityName = entityName, EntityId = "1", Action = "Create", ActorId = actorId, ActorName = "alice", OccurredAt = Now },
                new AuditLog { Id = Guid.NewGuid(), EntityName = entityName, EntityId = "2", Action = "Update", ActorId = actorId, ActorName = "alice", OccurredAt = Now.AddMinutes(1) },
                new AuditLog { Id = Guid.NewGuid(), EntityName = entityName, EntityId = "3", Action = "Delete", ActorId = Guid.NewGuid(), ActorName = "bob", OccurredAt = Now.AddMinutes(2) });
            await db.SaveChangesAsync();
            return 0;
        });

        var (items, total) = await RunAsync((repo, _) =>
            repo.GetPagedAsync(new AuditLogFilter(EntityName: entityName), page: 1, pageSize: 2, sortDesc: true));

        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("3", items[0].EntityId);
        Assert.Equal("2", items[1].EntityId);

        var (filteredByKeyword, keywordTotal) = await RunAsync((repo, _) =>
            repo.GetPagedAsync(new AuditLogFilter(EntityName: entityName, Keyword: "bob"), page: 1, pageSize: 20, sortDesc: true));
        Assert.Equal(1, keywordTotal);
        Assert.Equal("3", filteredByKeyword[0].EntityId);

        var (filteredByActor, actorTotal) = await RunAsync((repo, _) =>
            repo.GetPagedAsync(new AuditLogFilter(EntityName: entityName, ActorId: actorId), page: 1, pageSize: 20, sortDesc: false));
        Assert.Equal(2, actorTotal);
        Assert.Equal("1", filteredByActor[0].EntityId);
    }

    [PostgresFact]
    public async Task GetByIdAsync_ReturnsMatchingRow()
    {
        var id = Guid.NewGuid();
        await RunAsync(async (repo, db) =>
        {
            db.AuditLogs.Add(new AuditLog { Id = id, EntityName = "X", EntityId = "1", Action = "Create", OccurredAt = Now });
            await db.SaveChangesAsync();
            return 0;
        });

        var found = await RunAsync((repo, _) => repo.GetByIdAsync(id));
        var missing = await RunAsync((repo, _) => repo.GetByIdAsync(Guid.NewGuid()));

        Assert.NotNull(found);
        Assert.Equal(id, found!.Id);
        Assert.Null(missing);
    }
}
