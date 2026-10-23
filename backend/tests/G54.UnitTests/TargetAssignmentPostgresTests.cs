using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

/// <summary>
/// Runs the allocation against the real schema: the <c>validate_target_assignment</c> trigger, the unique indexes and the
/// <c>FOR UPDATE</c> lock on the parent, none of which the in-memory provider exercises.
/// </summary>
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
    public async Task FullChain_PassesTheDatabaseTriggers_AndIsStoredAsActiveOrgSplits()
    {
        var root = await RunAsync(s => s.CreateRootAsync(_adminAccount, Root(1000)));
        var level2 = await RunAsync(s => s.AllocateAsync(_adminAccount, root.Id, Split((_deptA, null, 600m), (_deptB, null, 400m))));
        var deptA = level2.Children[0];
        var level3 = await RunAsync(s => s.AllocateAsync(_adminAccount, deptA.Id, Split((_teamId, null, 200m), (null, _memberId, 400m))));

        Assert.Equal("ORG", level3.Children[0].SplitDimension);
        Assert.Equal("EMPLOYEE", level3.Children[1].OwnerType);
        Assert.Equal(600m, level3.Parent.AllocatedValue);
        Assert.Equal(0m, level3.Parent.RemainingValue);
        var reloaded = await RunAsync(s => s.GetAsync(_adminAccount, root.Id));
        Assert.Equal(1000m, reloaded.AllocatedValue);
        Assert.Equal("ACTIVE", reloaded.Status);
        Assert.NotNull(reloaded.ApprovedAt);
    }

    [PostgresFact]
    public async Task ASumMismatch_IsRefusedBeforeTheDatabaseAndLeavesNothingBehind()
    {
        var root = await RunAsync(s => s.CreateRootAsync(_adminAccount, Root(1000)));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RunAsync(s => s.AllocateAsync(_adminAccount, root.Id, Split((_deptA, null, 600m), (_deptB, null, 300m)))));

        Assert.Equal(ErrorCodes.TargetAllocationSumMismatch, ex.ErrorCode);
        Assert.Equal(0m, (await RunAsync(s => s.GetAsync(_adminAccount, root.Id))).AllocatedValue);
    }

    [PostgresFact]
    public async Task ADuplicateRoot_IsAConflict()
    {
        await RunAsync(s => s.CreateRootAsync(_adminAccount, Root()));

        await Assert.ThrowsAsync<ConflictException>(() => RunAsync(s => s.CreateRootAsync(_adminAccount, Root(5))));
    }

    [PostgresFact]
    public async Task ConcurrentSplitsOfTheSameParent_OnlyOneWins()
    {
        var root = await RunAsync(s => s.CreateRootAsync(_adminAccount, Root(1000)));

        var attempts = await Task.WhenAll(
            Attempt(root.Id, Split((_deptA, null, 600m), (_deptB, null, 400m))),
            Attempt(root.Id, Split((_deptA, null, 500m), (_deptB, null, 500m))));

        Assert.Single(attempts, outcome => outcome is null);
        Assert.IsType<ConflictException>(Assert.Single(attempts, outcome => outcome is not null));
        Assert.Equal(1000m, (await RunAsync(s => s.GetAsync(_adminAccount, root.Id))).AllocatedValue);
    }

    private async Task<Exception?> Attempt(Guid parentId, AllocateTargetDto dto)
    {
        try
        {
            await RunAsync(s => s.AllocateAsync(_adminAccount, parentId, dto));
            return null;
        }
        catch (Exception exception) when (exception is AppException)
        {
            return exception;
        }
    }
    private sealed class AdminPermissions : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = [new UserPermissionDto("target", "manage", "All", "TARGET_MANAGE")];
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
