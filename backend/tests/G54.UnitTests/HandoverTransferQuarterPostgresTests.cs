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
/// The targets the development seed really holds: each employee has a quarter target (a share of the department's
/// quarter) and that is split into monthly targets. Moving a work's expected result must change the quarter as well,
/// otherwise the months no longer add up to it and the database trigger refuses the save.
/// </summary>
public sealed class HandoverTransferQuarterPostgresTests : IAsyncLifetime
{
    /// <summary>20 Jan 2026, 15:00 in Vietnam: January is the handover month and February the next one.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 20, 8, 0, 0, TimeSpan.Zero);

    private string _database = null!;
    private ServiceProvider _provider = null!;
    private Guid _managerId;
    private Guid _giverId;
    private Guid _recipientId;
    private Guid _workId;
    private Guid _deptQuarterId;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_handoverq_{Guid.NewGuid():N}";
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
        var branch = Department("HQ-B", DepartmentLevelType.Branch, null);
        var dept = Department("HQ-D", DepartmentLevelType.Department, branch.Id);
        db.Departments.AddRange(branch, dept);
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "HQ-J", Title = "Job", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        db.JobPositions.Add(job);
        await db.SaveChangesAsync();

        var manager = Employee("HQ-MGR", dept.Id, job.Id, null);
        db.Employees.Add(manager);
        await db.SaveChangesAsync();
        var giver = Employee("HQ-A", dept.Id, job.Id, manager.Id);
        var recipient = Employee("HQ-B2", dept.Id, job.Id, manager.Id);
        db.Employees.AddRange(giver, recipient);
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = manager.Id, Username = "hq-mgr", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "HQ-G", Name = "G", Status = KpiStatus.Active, UpdatedAt = Now };
        db.KpiGroups.Add(group);
        await db.SaveChangesAsync();
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "HQ-SUM",
            Name = "Sum",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "HQ-WT", Name = "WT", Status = RecordStatus.Active, UpdatedAt = Now };
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

        var year = Period("2026", PeriodType.Year, null, new(2026, 1, 1), new(2026, 12, 31));
        var h1 = Period("2026-H1", PeriodType.HalfYear, year.Id, new(2026, 1, 1), new(2026, 6, 30));
        var q1 = Period("2026-Q1", PeriodType.Quarter, h1.Id, new(2026, 1, 1), new(2026, 3, 31));
        var jan = Period("2026-01", PeriodType.Month, q1.Id, new(2026, 1, 1), new(2026, 1, 31));
        var feb = Period("2026-02", PeriodType.Month, q1.Id, new(2026, 2, 1), new(2026, 2, 28));
        var mar = Period("2026-03", PeriodType.Month, q1.Id, new(2026, 3, 1), new(2026, 3, 31));
        foreach (var period in new[] { year, h1, q1, jan, feb, mar })
        {
            db.EvaluationPeriods.Add(period);
            await db.SaveChangesAsync();
        }

        // Department quarter 600 (ORG split) -> employee quarters 300 + 300 -> three monthly targets of 100 each (PERIOD split).
        var deptQuarter = Target(kpi.Id, q1.Id, null, dept.Id, null, null, 600m, q1.StartDate);
        db.TargetAssignments.Add(deptQuarter);
        await db.SaveChangesAsync();
        var quarters = new List<TargetAssignment>();
        foreach (var employee in new[] { giver, recipient })
        {
            var quarter = Target(kpi.Id, q1.Id, deptQuarter.Id, null, employee.Id, TargetSplitDimension.Org, 300m, q1.StartDate);
            quarters.Add(quarter);
            foreach (var month in new[] { jan, feb, mar })
            {
                db.TargetAssignments.Add(Target(kpi.Id, month.Id, quarter.Id, null, employee.Id, TargetSplitDimension.Period, 100m, month.StartDate));
            }
        }

        // The sum rule is checked when the save commits, so both quarters (and their months) go in together.
        db.TargetAssignments.AddRange(quarters);
        await db.SaveChangesAsync();
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "HQ-W1",
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

        (_managerId, _giverId, _recipientId, _workId, _deptQuarterId) = (manager.Id, giver.Id, recipient.Id, work.Id, deptQuarter.Id);
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

    private static TargetAssignment Target(
        Guid kpiId, Guid periodId, Guid? parentId, Guid? departmentId, Guid? employeeId, TargetSplitDimension? split, decimal value, DateOnly from) => new()
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = periodId,
            ParentAssignmentId = parentId,
            DepartmentId = departmentId,
            EmployeeId = employeeId,
            SplitDimension = split,
            Version = 1,
            TargetValue = value,
            Status = TargetAssignmentStatus.Active,
            CreatedAt = Now,
            AssignedAt = Now,
            ApprovedAt = Now,
            UpdatedAt = Now,
            EffectiveFrom = from,
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

    private static async Task<(decimal Quarter, string Months)> ActiveAsync(AppDbContext db, Guid employeeId)
    {
        var rows = await db.TargetAssignments
            .Where(t => t.EmployeeId == employeeId && t.Status == TargetAssignmentStatus.Active)
            .Select(t => new { t.EvaluationPeriod.PeriodType, t.EvaluationPeriod.Code, t.TargetValue })
            .ToListAsync();
        var quarter = rows.Single(r => r.PeriodType == PeriodType.Quarter).TargetValue;
        var months = string.Join(", ", rows.Where(r => r.PeriodType == PeriodType.Month).OrderBy(r => r.Code).Select(r => $"{r.Code}={r.TargetValue:0}"));
        return (quarter, months);
    }

    [PostgresFact]
    public async Task Create_MovesTheQuarterAndTheMonths_SoTheMonthsStillAddUpToTheQuarter()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await HandOverAsync(db);

        Assert.Equal("ACCEPTED", result.Status);
        var giver = await ActiveAsync(db, _giverId);
        var recipient = await ActiveAsync(db, _recipientId);
        Assert.Equal(240m, giver.Quarter);
        Assert.Equal("2026-01=70, 2026-02=70, 2026-03=100", giver.Months);
        Assert.Equal(360m, recipient.Quarter);
        Assert.Equal("2026-01=130, 2026-02=130, 2026-03=100", recipient.Months);
    }

    [PostgresFact]
    public async Task Create_LeavesTheDepartmentQuarterWhole()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await HandOverAsync(db);

        var children = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId == _deptQuarterId && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync();
        Assert.Equal(600m, children.Sum(t => t.TargetValue));
        Assert.Equal(600m, (await db.TargetAssignments.SingleAsync(t => t.Id == _deptQuarterId)).TargetValue);
    }

    [PostgresFact]
    public async Task Create_KeepsTheOldVersionOfThisMonthsTarget_AndChangesTheQuarterInPlace()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await HandOverAsync(db);

        var giverJanuary = await db.TargetAssignments
            .Where(t => t.EmployeeId == _giverId && t.EvaluationPeriod.Code == "2026-01")
            .OrderBy(t => t.Version)
            .ToListAsync();
        Assert.Equal([TargetAssignmentStatus.Superseded, TargetAssignmentStatus.Active], giverJanuary.Select(t => t.Status));
        Assert.Equal([100m, 70m], giverJanuary.Select(t => t.TargetValue));
        var giverQuarters = await db.TargetAssignments
            .Where(t => t.EmployeeId == _giverId && t.EvaluationPeriod.PeriodType == PeriodType.Quarter)
            .ToListAsync();
        Assert.Single(giverQuarters);
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
