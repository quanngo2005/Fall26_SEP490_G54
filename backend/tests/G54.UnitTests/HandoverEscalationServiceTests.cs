using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class HandoverEscalationServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Sla = TimeSpan.FromHours(4);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private Employee _manager = null!;
    private Employee _from = null!;
    private Employee _to = null!;

    public void Dispose() => _db.Dispose();

    private HandoverEscalationService Service() => new(_db, new FixedTime());

    private async Task SeedAsync(bool receiverHasManager = true)
    {
        var departmentId = Guid.NewGuid();
        _manager = Employee("MGR", departmentId, null);
        _from = Employee("FROM", departmentId, _manager.Id);
        _to = Employee("TO", departmentId, receiverHasManager ? _manager.Id : null);
        _db.Employees.AddRange(_manager, _from, _to);
        await _db.SaveChangesAsync();
    }

    private static Employee Employee(string code, Guid departmentId, Guid? managerId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = code,
        Email = $"{code.ToLowerInvariant()}@x.local",
        DepartmentId = departmentId,
        JobPositionId = Guid.NewGuid(),
        ManagerId = managerId,
        Status = EmployeeStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private async Task<TaskHandover> AddHandoverAsync(TimeSpan age, HandoverStatus status = HandoverStatus.Pending)
    {
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"W-{Guid.NewGuid():N}"[..12],
            Title = "Work",
            AssigneeEmployeeId = _from.Id,
            CreatedBy = _from.Id,
            Priority = WorkPriority.Medium,
            Status = WorkStatus.New,
            UpdatedAt = Now,
        };
        _db.Works.Add(work);
        var handover = new TaskHandover
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            FromEmployeeId = _from.Id,
            ToEmployeeId = _to.Id,
            Reason = "r",
            HandoverAt = Now - age,
            Status = status,
            UpdatedAt = Now - age,
        };
        _db.TaskHandovers.Add(handover);
        await _db.SaveChangesAsync();
        return handover;
    }

    [Fact]
    public async Task Escalates_APendingHandoverOlderThanTheSla_ToTheReceiversManager()
    {
        await SeedAsync();
        var handover = await AddHandoverAsync(TimeSpan.FromHours(5));

        var count = await Service().EscalateOverdueAsync(Sla);

        Assert.Equal(1, count);
        var stored = await _db.TaskHandovers.AsNoTracking().SingleAsync(h => h.Id == handover.Id);
        Assert.Equal(Now, stored.EscalatedAt);
        Assert.Equal(_manager.Id, stored.EscalatedToEmployeeId);
        Assert.Equal(Now, stored.UpdatedAt);
        Assert.Equal(HandoverStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task TheSlaBoundaryIsInclusive_AndAYoungerHandoverIsLeftAlone()
    {
        await SeedAsync();
        var exact = await AddHandoverAsync(Sla);
        var young = await AddHandoverAsync(Sla - TimeSpan.FromMinutes(1));

        Assert.Equal(1, await Service().EscalateOverdueAsync(Sla));

        Assert.NotNull((await _db.TaskHandovers.AsNoTracking().SingleAsync(h => h.Id == exact.Id)).EscalatedAt);
        Assert.Null((await _db.TaskHandovers.AsNoTracking().SingleAsync(h => h.Id == young.Id)).EscalatedAt);
    }

    [Theory]
    [InlineData(HandoverStatus.Accepted)]
    [InlineData(HandoverStatus.Rejected)]
    [InlineData(HandoverStatus.Cancelled)]
    public async Task OnlyPendingHandoversAreEscalated(HandoverStatus status)
    {
        await SeedAsync();
        await AddHandoverAsync(TimeSpan.FromHours(9), status);

        Assert.Equal(0, await Service().EscalateOverdueAsync(Sla));
    }

    [Fact]
    public async Task AHandoverIsEscalatedOnlyOnce()
    {
        await SeedAsync();
        var handover = await AddHandoverAsync(TimeSpan.FromHours(5));
        Assert.Equal(1, await Service().EscalateOverdueAsync(Sla));

        Assert.Equal(0, await new HandoverEscalationService(_db, new FixedTime(Now.AddHours(8))).EscalateOverdueAsync(Sla));

        Assert.Equal(Now, (await _db.TaskHandovers.AsNoTracking().SingleAsync(h => h.Id == handover.Id)).EscalatedAt);
    }

    [Fact]
    public async Task AReceiverWithoutAManager_IsSkippedAndStaysEligible()
    {
        await SeedAsync(receiverHasManager: false);
        var handover = await AddHandoverAsync(TimeSpan.FromHours(5));

        Assert.Equal(0, await Service().EscalateOverdueAsync(Sla));

        var stored = await _db.TaskHandovers.AsNoTracking().SingleAsync(h => h.Id == handover.Id);
        Assert.Null(stored.EscalatedAt);
        Assert.Null(stored.EscalatedToEmployeeId);
    }

    [Fact]
    public async Task EscalatesEveryOverdueHandoverInOneSweep()
    {
        await SeedAsync();
        await AddHandoverAsync(TimeSpan.FromHours(5));
        await AddHandoverAsync(TimeSpan.FromHours(30));

        Assert.Equal(2, await Service().EscalateOverdueAsync(Sla));
        Assert.Equal(2, await _db.TaskHandovers.CountAsync(h => h.EscalatedToEmployeeId == _manager.Id));
    }

    private sealed class FixedTime(DateTimeOffset? now = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now ?? Now;
    }
}
