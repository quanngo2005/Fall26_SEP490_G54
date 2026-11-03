using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.BusinessObjective;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// Runs the objective and KPI services on migrated PostgreSQL: the unique code indexes, the foreign keys that stop a
/// delete, the date check and the <c>kpi_status</c> labels only exist there. Skips when no Postgres is
/// available (see <see cref="TestPostgres"/>). Each test owns its database and does not retain its EF provider in the
/// process-wide cache (EF refuses more than twenty).
/// </summary>
public sealed class BusinessObjectivePostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    private string _database = null!;
    private ServiceProvider _provider = null!;
    private Guid _kpiGroupId;
    private Guid _kpiId;
    private Account _requester = null!;
    private Account _approver = null!;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_objective_{Guid.NewGuid():N}";
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
        var department = new Department { Id = Guid.NewGuid(), Code = "OB-D", Name = "Dept", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "OB-J", Title = "Job", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "OB-G", Name = "Group", Status = KpiStatus.Active, UpdatedAt = Now };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "OB-K",
            Name = "Kpi",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };
        db.AddRange(department, job, group);
        await db.SaveChangesAsync();
        db.KpiDefinitions.Add(kpi);

        Account NewAccount(string code)
        {
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = code,
                FullName = code,
                Email = $"{code.ToLowerInvariant()}@bank.com",
                DepartmentId = department.Id,
                JobPositionId = job.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            var account = new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "hash", LastSyncAt = Now, UpdatedAt = Now };
            db.AddRange(employee, account);
            return account;
        }

        _requester = NewAccount("OB-REQ");
        _approver = NewAccount("OB-APP");
        await db.SaveChangesAsync();
        _kpiGroupId = group.Id;
        _kpiId = kpi.Id;
    }

    private static string UniqueCode() => $"T-{Guid.NewGuid():N}"[..20];

    private static BusinessObjectiveSaveDto Save(string code) => new()
    {
        Code = code,
        Name = "Objective",
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
    };

    private async Task<T> RunAsync<T>(Func<BusinessObjectiveService, AppDbContext, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(new BusinessObjectiveService(db, new FakePermissionService(null), new FixedTime(Now)), db);
    }

    [PostgresFact]
    public async Task Create_DuplicateCode_IsConflict()
    {
        var code = UniqueCode();
        await RunAsync((service, _) => service.CreateObjectiveAsync(Save(code)));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => RunAsync((service, _) => service.CreateObjectiveAsync(Save(code))));

        Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
    }

    [PostgresFact]
    public async Task Create_ThenDelete_PersistsAndRemoves()
    {
        var created = await RunAsync((service, _) => service.CreateObjectiveAsync(Save(UniqueCode())));

        var stored = await RunAsync((_, db) => db.BusinessObjectives.AsNoTracking().SingleAsync(o => o.Id == created.Id));
        await RunAsync(async (service, _) =>
        {
            await service.DeleteObjectiveAsync(created.Id);
            return 0;
        });
        var remaining = await RunAsync((_, db) => db.BusinessObjectives.AsNoTracking().CountAsync(o => o.Id == created.Id));

        Assert.Equal(BusinessObjectiveStatus.Active, stored.Status);
        Assert.Equal(0, remaining);
    }

    [PostgresFact]
    public async Task Delete_ObjectiveWithALinkedKpi_IsInUseConflict()
    {
        var created = await RunAsync((service, _) => service.CreateObjectiveAsync(Save(UniqueCode())));
        await RunAsync(async (_, db) =>
        {
            db.ObjectiveKpis.Add(new ObjectiveKpi { Id = Guid.NewGuid(), BusinessObjectiveId = created.Id, KpiDefinitionId = _kpiId, UpdatedAt = Now });
            await db.SaveChangesAsync();
            return 0;
        });

        var ex = await Assert.ThrowsAsync<ConflictException>(() => RunAsync(async (service, _) =>
        {
            await service.DeleteObjectiveAsync(created.Id);
            return 0;
        }));

        Assert.Equal(ErrorCodes.BusinessObjectiveInUse, ex.ErrorCode);
    }

    [PostgresFact]
    public async Task TheDatabaseRejectsAnEndDateBeforeTheStartDate()
    {
        var created = await RunAsync((service, _) => service.CreateObjectiveAsync(Save(UniqueCode())));

        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(async (_, db) =>
        {
            var row = await db.BusinessObjectives.SingleAsync(o => o.Id == created.Id);
            row.EndDate = row.StartDate.AddDays(-1);
            await db.SaveChangesAsync();
            return 0;
        }));
    }

    [PostgresFact]
    public async Task KpiRequestApproval_PersistsEveryStatusAndTheRequester()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new KpiDefinitionService(db);

        var requested = await service.RequestKpiAsync(
            _requester.Id,
            new KpiRequestDto
            {
                RequestNote = "Missing from the catalog",
                Kpi = new KpiDefinitionSaveDto { KpiGroupId = _kpiGroupId, Code = UniqueCode(), Name = "Requested", Unit = "VND", AggregationType = "SUM", Direction = "HIGHER_BETTER" },
            });
        await service.ChangeStatusAsync(_requester.Id, requested.Id, new KpiStatusDto { Status = "PENDING_APPROVAL" });
        await service.RejectKpiAsync(_approver.Id, requested.Id, new RejectKpiDto { Reason = "Formula missing" });
        await service.ChangeStatusAsync(_requester.Id, requested.Id, new KpiStatusDto { Status = "PENDING_APPROVAL" });
        await service.ApproveKpiAsync(_approver.Id, requested.Id);

        var stored = await db.KpiDefinitions.AsNoTracking().SingleAsync(k => k.Id == requested.Id);
        Assert.Equal(KpiStatus.Active, stored.Status);
        Assert.Equal(_requester.EmployeeId, stored.RequestedBy);
        Assert.Equal(_requester.EmployeeId, stored.SubmittedBy);
        Assert.Null(stored.RejectReason);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
