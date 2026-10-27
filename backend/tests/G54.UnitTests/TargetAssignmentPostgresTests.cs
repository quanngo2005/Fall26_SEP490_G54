using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace G54.UnitTests;

/// <summary>Skips when no Postgres is available (see <see cref="TestPostgres"/>); under <c>CI=true</c> a missing Postgres fails instead.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (TestPostgres.AdminConnectionString is null)
        {
            Skip = TestPostgres.SkipReason;
        }
    }
}

/// <summary>Runs the allocation against the real schema, where the hierarchy, sum and uniqueness rules live (the in-memory provider has none).</summary>
public sealed class TargetAssignmentPostgresTests : IAsyncLifetime
{
    private string _database = null!;
    private string _connectionString = null!;
    private ServiceProvider _provider = null!;
    private Guid _adminAccount;
    private Guid _branchId;
    private Guid _deptA;
    private Guid _deptB;
    private Guid _teamId;
    private Guid _memberId;
    private Guid _kpiId;
    private Guid _periodId;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_target_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(_connectionString);
        // Each test owns a unique data source; do not retain its EF provider in the process-wide cache.
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddScoped(provider => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .EnableServiceProviderCaching(false).Options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPermissionService, AdminPermissions>();
        services.AddScoped<ITargetAssignmentService, TargetAssignmentService>();
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
        var branch = Department("PG-B", DepartmentLevelType.Branch, null, now);
        var deptA = Department("PG-DA", DepartmentLevelType.Department, branch.Id, now);
        var deptB = Department("PG-DB", DepartmentLevelType.Department, branch.Id, now);
        var team = Department("PG-T", DepartmentLevelType.Team, deptA.Id, now);
        db.Departments.AddRange(branch, deptA, deptB, team);

        var job = new JobPosition { Id = Guid.NewGuid(), Code = "PG-JOB", Title = "Job", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        db.JobPositions.Add(job);
        await db.SaveChangesAsync();

        var admin = Employee("PG-ADM", branch.Id, job.Id, now);
        var member = Employee("PG-MEM", deptA.Id, job.Id, now);
        db.Employees.AddRange(admin, member);
        await db.SaveChangesAsync();

        var account = new Account { Id = Guid.NewGuid(), EmployeeId = admin.Id, Username = "pg-adm", PasswordHash = "h", LastSyncAt = now, UpdatedAt = now };
        db.Accounts.Add(account);

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "PG-G", Name = "G", Status = KpiStatus.Active, UpdatedAt = now };
        db.KpiGroups.Add(group);
        await db.SaveChangesAsync();

        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "PG-SUM",
            Name = "Sum",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "PG-2026",
            Name = "2026",
            PeriodType = PeriodType.Year,
            Status = PeriodStatus.Open,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            UpdatedAt = now,
        };
        db.KpiDefinitions.Add(kpi);
        db.EvaluationPeriods.Add(period);
        await db.SaveChangesAsync();

        (_adminAccount, _branchId, _deptA, _deptB, _teamId, _memberId, _kpiId, _periodId) =
            (account.Id, branch.Id, deptA.Id, deptB.Id, team.Id, member.Id, kpi.Id, period.Id);
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

    private CreateTargetAssignmentDto Root(decimal value = 1000) => new()
    {
        KpiDefinitionId = _kpiId,
        EvaluationPeriodId = _periodId,
        DepartmentId = _branchId,
        TargetValue = value,
    };

    private static AllocateTargetDto Split(params (Guid? Department, Guid? Employee, decimal Value)[] rows) => new()
    {
        Allocations = [.. rows.Select(r => new TargetAllocationItemDto { DepartmentId = r.Department, EmployeeId = r.Employee, TargetValue = r.Value })],
    };

    private async Task<T> RunAsync<T>(Func<ITargetAssignmentService, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ITargetAssignmentService>());
    }

    [PostgresFact]
    public async Task FullChain_BranchToDepartmentToTeamAndEmployee_IsStoredAsActiveOrgSplits()
    {
        var root = await RunAsync(s => s.CreateRootAsync(_adminAccount, Root(1000)));
        var level2 = await RunAsync(s => s.AllocateAsync(_adminAccount, root.Id, Split((_deptA, null, 600m), (_deptB, null, 400m))));
        var level3 = await RunAsync(s => s.AllocateAsync(_adminAccount, level2[0].Id, Split((_teamId, null, 200m), (null, _memberId, 400m))));

        Assert.All(level3, child => Assert.Equal("ORG", child.SplitDimension));
        Assert.Equal(level2[0].Id, level3[0].ParentAssignmentId);
        Assert.Equal(_memberId, level3[1].EmployeeId);
        Assert.Equal("ACTIVE", (await RunAsync(s => s.GetAsync(_adminAccount, root.Id))).Status);
    }

    [PostgresFact]
    public async Task ASumMismatch_IsRejectedByTheDatabaseAndLeavesNothingBehind()
    {
        var root = await RunAsync(s => s.CreateRootAsync(_adminAccount, Root(1000)));

        await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync(s => s.AllocateAsync(_adminAccount, root.Id, Split((_deptA, null, 600m), (_deptB, null, 300m)))));

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.TargetAssignments.CountAsync());
    }

    [PostgresFact]
    public async Task AChildOutsideTheParentsOrganisation_IsRejectedByTheDatabase()
    {
        var root = await RunAsync(s => s.CreateRootAsync(_adminAccount, Root(1000)));
        var level2 = await RunAsync(s => s.AllocateAsync(_adminAccount, root.Id, Split((_deptA, null, 1000m))));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => RunAsync(s => s.AllocateAsync(_adminAccount, level2[0].Id, Split((_deptB, null, 1000m)))));
    }
    [PostgresFact]
    public async Task ADuplicateRoot_IsRejectedByTheDatabase()
    {
        await RunAsync(s => s.CreateRootAsync(_adminAccount, Root()));

        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(s => s.CreateRootAsync(_adminAccount, Root(5))));
    }

    private sealed class AdminPermissions : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
