using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace G54.UnitTests;

/// <summary>Runs the escalation against the real schema, where the migration, the foreign key and the pairing check live.</summary>
public sealed class HandoverEscalationPostgresTests : IAsyncLifetime
{
    private string _database = null!;
    private ServiceProvider _provider = null!;
    private Guid _managerId;
    private Guid _fromId;
    private Guid _toId;
    private Guid _workId;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_handover_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(connectionString);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IHandoverEscalationService, HandoverEscalationService>();
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
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "PG-D",
            Name = "D",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "PG-JOB", Title = "Job", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        db.Departments.Add(department);
        db.JobPositions.Add(job);
        await db.SaveChangesAsync();

        var manager = Employee("PG-MGR", department.Id, job.Id, null, now);
        db.Employees.Add(manager);
        await db.SaveChangesAsync();
        var from = Employee("PG-FROM", department.Id, job.Id, manager.Id, now);
        var to = Employee("PG-TO", department.Id, job.Id, manager.Id, now);
        db.Employees.AddRange(from, to);
        await db.SaveChangesAsync();

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "PG-W",
            Title = "W",
            AssigneeEmployeeId = from.Id,
            CreatedBy = from.Id,
            Priority = WorkPriority.Medium,
            Status = WorkStatus.New,
            UpdatedAt = now,
        };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        (_managerId, _fromId, _toId, _workId) = (manager.Id, from.Id, to.Id, work.Id);
    }

    private static Employee Employee(string code, Guid departmentId, Guid jobId, Guid? managerId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = code,
        Email = $"{code.ToLowerInvariant()}@x.local",
        DepartmentId = departmentId,
        JobPositionId = jobId,
        ManagerId = managerId,
        Status = EmployeeStatus.Active,
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private async Task<T> RunAsync<T>(Func<AppDbContext, IHandoverEscalationService, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IHandoverEscalationService>());
    }

    private Task<Guid> AddHandoverAsync(TimeSpan age, HandoverStatus status = HandoverStatus.Pending) => RunAsync(async (db, _) =>
    {
        var at = DateTimeOffset.UtcNow - age;
        var handover = new TaskHandover
        {
            Id = Guid.NewGuid(),
            WorkId = _workId,
            FromEmployeeId = _fromId,
            ToEmployeeId = _toId,
            Reason = "r",
            HandoverAt = at,
            Status = status,
            AcceptedAt = status == HandoverStatus.Accepted ? at.AddMinutes(1) : null,
            UpdatedAt = at,
        };
        db.TaskHandovers.Add(handover);
        await db.SaveChangesAsync();
        return handover.Id;
    });

    [PostgresFact]
    public async Task AnOverduePendingHandover_IsEscalatedToTheManagerAndPersisted()
    {
        var overdue = await AddHandoverAsync(TimeSpan.FromHours(5));
        var young = await AddHandoverAsync(TimeSpan.FromHours(1));
        var accepted = await AddHandoverAsync(TimeSpan.FromHours(6), HandoverStatus.Accepted);

        var count = await RunAsync((_, service) => service.EscalateOverdueAsync(TimeSpan.FromHours(4)));

        Assert.Equal(1, count);
        await RunAsync(async (db, _) =>
        {
            var rows = await db.TaskHandovers.AsNoTracking().ToDictionaryAsync(h => h.Id);
            Assert.Equal(_managerId, rows[overdue].EscalatedToEmployeeId);
            Assert.NotNull(rows[overdue].EscalatedAt);
            Assert.Null(rows[young].EscalatedAt);
            Assert.Null(rows[accepted].EscalatedAt);
            return 0;
        });
    }

    [PostgresFact]
    public async Task ASecondSweepEscalatesNothingMore()
    {
        await AddHandoverAsync(TimeSpan.FromHours(5));

        Assert.Equal(1, await RunAsync((_, service) => service.EscalateOverdueAsync(TimeSpan.FromHours(4))));
        Assert.Equal(0, await RunAsync((_, service) => service.EscalateOverdueAsync(TimeSpan.FromHours(4))));
    }

    [PostgresFact]
    public async Task TheDatabaseRejectsHalfAnEscalation()
    {
        var id = await AddHandoverAsync(TimeSpan.FromHours(5));

        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(async (db, _) =>
        {
            var handover = await db.TaskHandovers.SingleAsync(h => h.Id == id);
            handover.EscalatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return 0;
        }));
    }
}
