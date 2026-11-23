using G54.BLL.Common;
using G54.BLL.Dtos.Handover;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// A handover moves the work's expected result between two employees' monthly targets against the real schema, where the
/// triggers (children sum to the parent, versions follow each other) and the unique indexes live.
/// </summary>
public sealed class HandoverTransferPostgresTests : IAsyncLifetime
{
    /// <summary>20 Jan 2026, 15:00 in Vietnam: January is the handover month and February the next one.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 20, 8, 0, 0, TimeSpan.Zero);

    private string _database = null!;
    private ServiceProvider _provider = null!;
    private Guid _managerId;
    private Guid _giverId;
    private Guid _recipientId;
    private Guid _kpiId;
    private Guid _workId;
    private Guid _janParentId;
    private Guid _febParentId;

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

        var services = new ServiceCollection();
        services.AddDataAccess(new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString);
        // Each test owns a unique data source; do not retain its EF provider in the process-wide cache.
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
        var branch = Department("HP-B", DepartmentLevelType.Branch, null);
        var dept = Department("HP-D", DepartmentLevelType.Department, branch.Id);
        db.Departments.AddRange(branch, dept);
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "HP-J", Title = "Job", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        db.JobPositions.Add(job);
        await db.SaveChangesAsync();

        var manager = Employee("HP-MGR", dept.Id, job.Id, null);
        db.Employees.Add(manager);
        await db.SaveChangesAsync();
        var giver = Employee("HP-A", dept.Id, job.Id, manager.Id);
        var recipient = Employee("HP-B2", dept.Id, job.Id, manager.Id);
        db.Employees.AddRange(giver, recipient);
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = manager.Id, Username = "hp-mgr", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "HP-G", Name = "G", Status = KpiStatus.Active, UpdatedAt = Now };
        db.KpiGroups.Add(group);
        await db.SaveChangesAsync();
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "HP-SUM",
            Name = "Sum",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "HP-WT", Name = "WT", Status = RecordStatus.Active, UpdatedAt = Now };
        db.KpiDefinitions.Add(kpi);
        db.WorkTypes.Add(workType);
        await db.SaveChangesAsync();
        db.WorkTypeKpis.Add(new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = kpi.Id,
            IsRequired = true,
            EffectiveFrom = new DateOnly(2025, 1, 1),
            UpdatedAt = Now,
        });

        // The schema enforces YEAR > HALF_YEAR > QUARTER > MONTH.
        var year = Period("2026", PeriodType.Year, null, new(2026, 1, 1), new(2026, 12, 31));
        var h1 = Period("2026-H1", PeriodType.HalfYear, year.Id, new(2026, 1, 1), new(2026, 6, 30));
        var q1 = Period("2026-Q1", PeriodType.Quarter, h1.Id, new(2026, 1, 1), new(2026, 3, 31));
        var jan = Period("2026-01", PeriodType.Month, q1.Id, new(2026, 1, 1), new(2026, 1, 31));
        var feb = Period("2026-02", PeriodType.Month, q1.Id, new(2026, 2, 1), new(2026, 2, 28));
        foreach (var period in new[] { year, h1, q1, jan, feb })
        {
            db.EvaluationPeriods.Add(period);
            await db.SaveChangesAsync();
        }

        // The department's monthly target (200) is split between the two employees (100 + 100).
        var janParent = DepartmentTarget(kpi.Id, dept.Id, jan);
        var febParent = DepartmentTarget(kpi.Id, dept.Id, feb);
        db.TargetAssignments.AddRange(janParent, febParent);
        await db.SaveChangesAsync();
        foreach (var (parent, month) in new[] { (janParent, jan), (febParent, feb) })
        {
            db.TargetAssignments.Add(EmployeeTarget(kpi.Id, giver.Id, month, parent.Id));
            db.TargetAssignments.Add(EmployeeTarget(kpi.Id, recipient.Id, month, parent.Id));
        }

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "HP-W1",
            Title = "A1",
            WorkTypeId = workType.Id,
            AssigneeEmployeeId = giver.Id,
            CreatedBy = manager.Id,
            TargetValue = 30m,
            Status = WorkStatus.InProgress,
            Priority = WorkPriority.Medium,
            AssignedAt = Now,
            UpdatedAt = Now,
        };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        (_managerId, _giverId, _recipientId, _kpiId, _workId, _janParentId, _febParentId) =
            (manager.Id, giver.Id, recipient.Id, kpi.Id, work.Id, janParent.Id, febParent.Id);
    }

    private static Department Department(string code, DepartmentLevelType level, Guid? parent) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        LevelType = level,
        ParentDepartmentId = parent,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static Employee Employee(string code, Guid departmentId, Guid jobId, Guid? managerId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = code,
        Email = $"{code.ToLowerInvariant()}@x.local",
        DepartmentId = departmentId,
        JobPositionId = jobId,
        ManagerId = managerId,
        Status = EmployeeStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static EvaluationPeriod Period(string code, PeriodType type, Guid? parent, DateOnly start, DateOnly end) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        PeriodType = type,
        ParentPeriodId = parent,
        StartDate = start,
        EndDate = end,
        Status = PeriodStatus.Open,
        UpdatedAt = Now,
    };

    private static TargetAssignment DepartmentTarget(Guid kpiId, Guid departmentId, EvaluationPeriod month) => new()
    {
        Id = Guid.NewGuid(),
        KpiDefinitionId = kpiId,
        DepartmentId = departmentId,
        EvaluationPeriodId = month.Id,
        Version = 1,
        TargetValue = 200m,
        Status = TargetAssignmentStatus.Active,
        CreatedAt = Now,
        AssignedAt = Now,
        ApprovedAt = Now,
        UpdatedAt = Now,
        EffectiveFrom = month.StartDate,
    };

    private static TargetAssignment EmployeeTarget(Guid kpiId, Guid employeeId, EvaluationPeriod month, Guid parentId) => new()
    {
        Id = Guid.NewGuid(),
        KpiDefinitionId = kpiId,
        EmployeeId = employeeId,
        EvaluationPeriodId = month.Id,
        ParentAssignmentId = parentId,
        SplitDimension = TargetSplitDimension.Org,
        Version = 1,
        TargetValue = 100m,
        Status = TargetAssignmentStatus.Active,
        CreatedAt = Now,
        AssignedAt = Now,
        ApprovedAt = Now,
        UpdatedAt = Now,
        EffectiveFrom = month.StartDate,
    };

    private async Task<HandoverDetailDto> HandOverAsync(AppDbContext db) =>
        await new HandoverService(
            db,
            new ManagerPermission(),
            new RecordingNotifier(),
            Options.Create(new SystemParametersOptions()),
            NullLogger<HandoverService>.Instance,
            new FixedTime())
            .CreateHandoverAsync(
                _workId,
                _managerId,
                new CreateHandoverRequestDto { ToEmployeeId = _recipientId, Reason = "Giu tien do KPI", Amount = 1_000m });

    private static Task<List<TargetAssignment>> TargetsAsync(AppDbContext db, Guid employeeId, int month) =>
        db.TargetAssignments
            .Where(t => t.EmployeeId == employeeId && t.EvaluationPeriod.StartDate.Month == month)
            .OrderBy(t => t.Version)
            .ToListAsync();

    [PostgresFact]
    public async Task Create_MovesTheExpectedResultBetweenTheTargets_WithoutBreakingTheParentSumOrVersionRules()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await HandOverAsync(db);

        Assert.Equal("ACCEPTED", result.Status);
        var giverJan = await TargetsAsync(db, _giverId, 1);
        Assert.Equal([TargetAssignmentStatus.Superseded, TargetAssignmentStatus.Active], giverJan.Select(t => t.Status));
        Assert.Equal([100m, 70m], giverJan.Select(t => t.TargetValue));
        Assert.Equal(new DateOnly(2026, 1, 19), giverJan[0].EffectiveTo);
        Assert.Equal(new DateOnly(2026, 1, 20), giverJan[1].EffectiveFrom);
        Assert.Equal(giverJan[0].Id, giverJan[1].PreviousVersionId);
        Assert.Equal([100m, 130m], (await TargetsAsync(db, _recipientId, 1)).Select(t => t.TargetValue));

        var giverFeb = await TargetsAsync(db, _giverId, 2);
        Assert.Equal(70m, Assert.Single(giverFeb).TargetValue);
        Assert.Equal(130m, Assert.Single(await TargetsAsync(db, _recipientId, 2)).TargetValue);

        foreach (var parentId in new[] { _janParentId, _febParentId })
        {
            var children = await db.TargetAssignments
                .Where(t => t.ParentAssignmentId == parentId && t.Status == TargetAssignmentStatus.Active)
                .ToListAsync();
            Assert.Equal(200m, children.Sum(t => t.TargetValue));
        }
    }

    [PostgresFact]
    public async Task Create_WhenTheGiverHasNoTargetThisMonth_StillMovesTheWork()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TargetAssignments.RemoveRange(await db.TargetAssignments.Where(t => t.EmployeeId == _giverId && t.EvaluationPeriod.StartDate.Month == 1).ToListAsync());
        db.TargetAssignments.Single(t => t.Id == _janParentId).TargetValue = 100m;
        await db.SaveChangesAsync();

        await HandOverAsync(db);

        Assert.Equal(_recipientId, (await db.Works.SingleAsync(w => w.Id == _workId)).AssigneeEmployeeId);
        Assert.Equal(100m, Assert.Single(await TargetsAsync(db, _recipientId, 1)).TargetValue);
        Assert.Equal(130m, Assert.Single(await TargetsAsync(db, _recipientId, 2)).TargetValue);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ManagerPermission : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(resource == "handover" && action == "set_share");

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
