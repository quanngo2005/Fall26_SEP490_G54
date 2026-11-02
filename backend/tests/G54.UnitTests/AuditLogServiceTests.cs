using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.AuditLog;
using G54.BLL.Services;
using G54.DAL.Entities;
using G54.DAL.Repositories;

namespace G54.UnitTests;

public sealed class AuditLogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPagedAsync_MapsItemsAndPagination()
    {
        var repo = new FakeAuditLogRepository(
            [
                new AuditLog
                {
                    Id = Guid.NewGuid(),
                    EntityName = "RiskEvent",
                    EntityId = "1",
                    Action = "Create",
                    OccurredAt = Now,
                    ChangedColumns = "[\"status\"]",
                },
            ],
            totalCount: 1);
        var service = new AuditLogService(repo);

        var result = await service.GetPagedAsync(new AuditLogQueryDto { EntityName = "RiskEvent" });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Single(result.Items);
        Assert.Equal("RiskEvent", result.Items[0].EntityName);
        Assert.Equal(["status"], result.Items[0].ChangedColumns);
    }

    [Fact]
    public async Task GetPagedAsync_FromAfterTo_ThrowsValidation()
    {
        var service = new AuditLogService(new FakeAuditLogRepository([], 0));

        await Assert.ThrowsAsync<ValidationException>(() => service.GetPagedAsync(new AuditLogQueryDto
        {
            From = Now,
            To = Now.AddDays(-1),
        }));
    }

    [Fact]
    public async Task GetPagedAsync_InvalidAction_ThrowsValidation()
    {
        var service = new AuditLogService(new FakeAuditLogRepository([], 0));

        await Assert.ThrowsAsync<ValidationException>(() => service.GetPagedAsync(new AuditLogQueryDto
        {
            Action = "Purge",
        }));
    }

    [Fact]
    public async Task GetPagedAsync_PageSizeTooLarge_ThrowsValidation()
    {
        var service = new AuditLogService(new FakeAuditLogRepository([], 0));

        await Assert.ThrowsAsync<ValidationException>(() => service.GetPagedAsync(new AuditLogQueryDto
        {
            PageSize = 1000,
        }));
    }

    [Fact]
    public async Task GetByIdAsync_Missing_ThrowsNotFound()
    {
        var service = new AuditLogService(new FakeAuditLogRepository([], 0));

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsDetail()
    {
        var id = Guid.NewGuid();
        var repo = new FakeAuditLogRepository(
            [
                new AuditLog
                {
                    Id = id,
                    EntityName = "RiskEvent",
                    EntityId = "1",
                    Action = "Update",
                    OccurredAt = Now,
                    OldValues = "{\"status\":\"OPEN\"}",
                    NewValues = "{\"status\":\"CONFIRMED\"}",
                },
            ],
            totalCount: 1);
        var service = new AuditLogService(repo);

        var detail = await service.GetByIdAsync(id);

        Assert.Equal(id, detail.Id);
        Assert.NotNull(detail.OldValues);
        Assert.NotNull(detail.NewValues);
    }

    private sealed class FakeAuditLogRepository(List<AuditLog> items, int totalCount) : IAuditLogRepository
    {
        public Task<(List<AuditLog> Items, int TotalCount)> GetPagedAsync(
            AuditLogFilter filter, int page, int pageSize, bool sortDesc, CancellationToken ct = default) =>
            Task.FromResult((items, totalCount));

        public Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(items.SingleOrDefault(a => a.Id == id));

        public Task AddRangeAsync(IEnumerable<AuditLog> logs, CancellationToken ct = default)
        {
            items.AddRange(logs);
            return Task.CompletedTask;
        }
    }
}
