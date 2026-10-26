using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace G54.UnitTests;

/// <summary>One migrated Postgres database for <see cref="UnitOfWorkTransactionTests"/>.</summary>
public sealed class UnitOfWorkPostgresFixture : IAsyncLifetime
{
    private string? _database;

    public ServiceProvider? Provider { get; private set; }

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_uow_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(connectionString);
        Provider = services.BuildServiceProvider();

        await using var scope = Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Provider is null)
        {
            return;
        }

        await Provider.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Transaction behaviour of <see cref="UnitOfWork"/>. The in-memory provider has no transactions, so these run against Postgres.
/// Each test uses its own scope and unique job codes, so the tests share the class database.
/// Skips when no Postgres is available (see <see cref="TestPostgres"/>); under <c>CI=true</c> a missing Postgres fails instead.
/// </summary>
public sealed class UnitOfWorkTransactionTests(UnitOfWorkPostgresFixture fixture) : IClassFixture<UnitOfWorkPostgresFixture>
{
    private ServiceProvider Provider => fixture.Provider!;

    [PostgresFact]
    public async Task BeginTransactionAsync_ReturnsTheActiveTransaction_WhenCalledTwice()
    {
        await using var scope = Provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var first = await uow.BeginTransactionAsync();
        var second = await uow.BeginTransactionAsync();

        Assert.Same(first, second);
        await uow.RollbackTransactionAsync();
    }

    [PostgresFact]
    public async Task CommitTransactionAsync_PersistsTheChangesMadeInsideIt()
    {
        var code = NewCode("COMMIT");
        await using (var scope = Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await uow.JobPositions.AddAsync(JobPosition(code));
            await uow.CommitTransactionAsync();
        }

        Assert.Equal(1, await CountJobsAsync(code));
    }

    [PostgresFact]
    public async Task RollbackTransactionAsync_DiscardsChangesThatWereAlreadyFlushed()
    {
        var code = NewCode("ROLL");
        await using (var scope = Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await uow.JobPositions.AddAsync(JobPosition(code));
            await uow.SaveChangesAsync();
            await uow.RollbackTransactionAsync();
        }

        Assert.Equal(0, await CountJobsAsync(code));
    }

    [PostgresFact]
    public async Task CommitTransactionAsync_Throws_WhenNoTransactionIsActive()
    {
        await using var scope = Provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.CommitTransactionAsync());
    }

    [PostgresFact]
    public async Task RollbackTransactionAsync_IsANoOp_WhenNoTransactionIsActive()
    {
        await using var scope = Provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.RollbackTransactionAsync();
    }

    [PostgresFact]
    public async Task ExecuteInTransactionAsync_RollsBackAndRethrows_WhenTheActionFails()
    {
        var code = NewCode("FAIL");
        await using (var scope = Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInTransactionAsync(async cancellationToken =>
            {
                await uow.JobPositions.AddAsync(JobPosition(code), cancellationToken);
                throw new InvalidOperationException("business rule violated");
            }));
        }

        Assert.Equal(0, await CountJobsAsync(code));
    }

    [PostgresFact]
    public async Task ExecuteInTransactionAsync_CommitsTheChanges_WhenTheActionSucceeds()
    {
        var code = NewCode("OK");
        await using (var scope = Provider.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.ExecuteInTransactionAsync(async cancellationToken =>
            {
                await uow.JobPositions.AddAsync(JobPosition(code), cancellationToken);
            });
        }

        Assert.Equal(1, await CountJobsAsync(code));
    }

    [PostgresFact]
    public async Task ExecuteInTransactionAsync_ReturnsTheActionResult_AndCommits()
    {
        var code = NewCode("RES");
        await using var scope = Provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var createdId = await uow.ExecuteInTransactionAsync(async cancellationToken =>
        {
            var job = await uow.JobPositions.AddAsync(JobPosition(code), cancellationToken);
            return job.Id;
        });

        Assert.NotEqual(Guid.Empty, createdId);
        Assert.Equal(1, await CountJobsAsync(code));
    }

    private static string NewCode(string purpose) => $"UOW-{purpose}-{Guid.NewGuid():N}"[..20];

    private static JobPosition JobPosition(string code)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = "Unit of work job",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
    }

    private async Task<int> CountJobsAsync(string code)
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobPositions.CountAsync(job => job.Code == code);
    }
}
