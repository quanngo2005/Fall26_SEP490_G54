using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace G54.UnitTests;

public sealed class TargetChangeLogServiceTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(AppDbContext Db, Guid KpiId)> SeedAsync()
    {
        var db = CreateDb();
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group" };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI_1",
            Name = "KPI One",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        };
        db.KpiGroups.Add(group);
        db.KpiDefinitions.Add(kpi);
        await db.SaveChangesAsync();
        return (db, kpi.Id);
    }

    [Fact]
    public async Task LogAsync_PersistsEntry()
    {
        var (db, kpiId) = await SeedAsync();
        var service = new TargetChangeLogService(db, TimeProvider.System);
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.LogAsync(
            actorId, TargetChangeLogAction.Adjust, tier: 3, kpiId: kpiId, unit: "VND", period: "YEAR",
            oldValue: 100m, newValue: 150m, basis: "MANUAL", employeeId: employeeId, year: 2027);

        var logs = await service.GetByEmployeeYearAsync(employeeId, 2027);
        var entry = Assert.Single(logs);
        Assert.Equal(TargetChangeLogAction.Adjust, entry.Action);
        Assert.Equal(100m, entry.OldValue);
        Assert.Equal(150m, entry.NewValue);
        Assert.Equal(actorId, entry.ActorId);
    }

    [Fact]
    public async Task GetByEmployeeYearAsync_FiltersByEmployeeAndYear_NewestFirst()
    {
        var (db, kpiId) = await SeedAsync();
        var service = new TargetChangeLogService(db, TimeProvider.System);
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        await service.LogAsync(Guid.NewGuid(), TargetChangeLogAction.Publish, 3, kpiId, "VND", "YEAR", null, 100m, employeeId: employeeId, year: 2027);
        await service.LogAsync(Guid.NewGuid(), TargetChangeLogAction.Adjust, 3, kpiId, "VND", "YEAR", 100m, 120m, employeeId: employeeId, year: 2027);
        await service.LogAsync(Guid.NewGuid(), TargetChangeLogAction.Publish, 3, kpiId, "VND", "YEAR", null, 90m, employeeId: otherEmployeeId, year: 2027);
        await service.LogAsync(Guid.NewGuid(), TargetChangeLogAction.Publish, 3, kpiId, "VND", "YEAR", null, 100m, employeeId: employeeId, year: 2026);

        var logs = await service.GetByEmployeeYearAsync(employeeId, 2027);

        Assert.Equal(2, logs.Count);
        Assert.Equal(TargetChangeLogAction.Adjust, logs[0].Action);
    }
}
