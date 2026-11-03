using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.BusinessObjective;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace G54.UnitTests;

/// <summary>
/// Runs the objective and KPI services on migrated PostgreSQL: the unique code indexes, the foreign keys that stop a
/// delete, the <c>priority_weight</c> check and the <c>kpi_status</c> labels only exist there. Skips when no Postgres is
/// available (see <see cref="TestPostgres"/>).
/// </summary>
public sealed class BusinessObjectivePostgresTests(DemoSeedDatabaseFixture fixture) : IClassFixture<DemoSeedDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

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
        await using var scope = fixture.Provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new BusinessObjectiveService(db, scope.ServiceProvider.GetRequiredService<IPermissionService>(), new FixedTime(Now));
        return await action(service, db);
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
        var created = await RunAsync((service, _) => service.CreateObjectiveAsync(Save(UniqueCode()) with { PriorityWeight = 7.5m }));

        var stored = await RunAsync((_, db) => db.BusinessObjectives.AsNoTracking().SingleAsync(o => o.Id == created.Id));
        await RunAsync(async (service, _) =>
        {
            await service.DeleteObjectiveAsync(created.Id);
            return 0;
        });
        var remaining = await RunAsync((_, db) => db.BusinessObjectives.AsNoTracking().CountAsync(o => o.Id == created.Id));

        Assert.Equal(BusinessObjectiveStatus.Active, stored.Status);
        Assert.Equal(7.5m, stored.PriorityWeight);
        Assert.Equal(0, remaining);
    }

    [PostgresFact]
    public async Task Delete_ObjectiveWithALinkedKpi_IsInUseConflict()
    {
        var created = await RunAsync((service, _) => service.CreateObjectiveAsync(Save(UniqueCode())));
        await RunAsync(async (_, db) =>
        {
            var kpi = await db.KpiDefinitions.AsNoTracking().FirstAsync();
            db.ObjectiveKpis.Add(new ObjectiveKpi { Id = Guid.NewGuid(), BusinessObjectiveId = created.Id, KpiDefinitionId = kpi.Id, UpdatedAt = Now });
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
    public async Task TheDatabaseRejectsANegativePriorityWeight()
    {
        var created = await RunAsync((service, _) => service.CreateObjectiveAsync(Save(UniqueCode())));

        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(async (_, db) =>
        {
            var row = await db.BusinessObjectives.SingleAsync(o => o.Id == created.Id);
            row.PriorityWeight = -1m;
            await db.SaveChangesAsync();
            return 0;
        }));
    }

    [PostgresFact]
    public async Task KpiRequestApproval_PersistsEveryStatusAndTheRequester()
    {
        await using var scope = fixture.Provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new KpiDefinitionService(db);
        var requester = await db.Accounts.AsNoTracking().OrderBy(a => a.Username).FirstAsync();
        var approver = await db.Accounts.AsNoTracking().OrderByDescending(a => a.Username).FirstAsync();
        var group = await db.KpiGroups.AsNoTracking().FirstAsync();
        var code = UniqueCode();

        var requested = await service.RequestKpiAsync(
            requester.Id,
            new KpiRequestDto
            {
                RequestNote = "Missing from the catalog",
                Kpi = new KpiDefinitionSaveDto { KpiGroupId = group.Id, Code = code, Name = "Requested", Unit = "VND", AggregationType = "SUM", Direction = "HIGHER_BETTER" },
            });
        await service.ChangeStatusAsync(requester.Id, requested.Id, new KpiStatusDto { Status = "PENDING_APPROVAL" });
        await service.RejectKpiAsync(approver.Id, requested.Id, new RejectKpiDto { Reason = "Formula missing" });
        await service.ChangeStatusAsync(requester.Id, requested.Id, new KpiStatusDto { Status = "PENDING_APPROVAL" });
        await service.ApproveKpiAsync(approver.Id, requested.Id);

        var stored = await db.KpiDefinitions.AsNoTracking().SingleAsync(k => k.Id == requested.Id);
        Assert.Equal(KpiStatus.Active, stored.Status);
        Assert.Equal(requester.EmployeeId, stored.RequestedBy);
        Assert.Null(stored.RejectReason);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
