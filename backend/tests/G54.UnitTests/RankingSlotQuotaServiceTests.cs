using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameter;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class RankingSlotQuotaServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Guid _departmentId = Guid.NewGuid();

    public RankingSlotQuotaServiceTests()
    {
        _db.Departments.Add(new Department { Id = _departmentId, Code = "DEP-SLOT", Name = "Phòng slot" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Get_WhenNothingConfigured_ReturnsEveryTierWithNullHeadcount()
    {
        var service = new RankingSlotQuotaService(_db);

        var result = await service.GetAsync(_departmentId);

        Assert.Equal(_departmentId, result.DepartmentId);
        Assert.Equal(
            new (string, int?)[] { ("A1", null), ("A2", null), ("A3", null), ("B", null), ("C", null) },
            result.Slots.Select(s => (s.TierCode, s.Headcount)).ToList());
    }

    [Fact]
    public async Task Update_SavesEachTierAndReturnsThem()
    {
        var service = new RankingSlotQuotaService(_db);

        var result = await service.UpdateAsync(Request(a1: 2, a2: 3, a3: 4, b: 6));

        Assert.Equal(
            new (string, int?)[] { ("A1", 2), ("A2", 3), ("A3", 4), ("B", 6), ("C", 5) },
            result.Slots.Select(s => (s.TierCode, s.Headcount)).ToList());
        Assert.Equal(5, await _db.RankingSlotQuotas.CountAsync());
    }

    [Fact]
    public async Task Update_OverwritesExistingRowsInsteadOfDuplicating()
    {
        var service = new RankingSlotQuotaService(_db);
        await service.UpdateAsync(Request(a1: 2, a2: 3, a3: 4, b: 6));

        var result = await service.UpdateAsync(Request(a1: 1, a2: 1, a3: 1, b: 1, c: 1));

        Assert.Equal(5, await _db.RankingSlotQuotas.CountAsync());
        Assert.Equal(new int?[] { 1, 1, 1, 1, 1 }, result.Slots.Select(s => s.Headcount).ToList());
    }

    [Fact]
    public async Task Update_RejectsNegativeHeadcount()
    {
        var service = new RankingSlotQuotaService(_db);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(Request(a1: -1, a2: 3, a3: 4, b: 6)));

        Assert.Contains(ex.Errors!, e => e.Contains("không âm"));
    }

    [Fact]
    public async Task Update_RejectsMissingHeadcount()
    {
        var service = new RankingSlotQuotaService(_db);
        var request = new UpdateRankingSlotQuotasDto(_departmentId,
        [
            new RankingSlotQuotaDto("A1", 2),
            new RankingSlotQuotaDto("A2", null),
            new RankingSlotQuotaDto("A3", 4),
            new RankingSlotQuotaDto("B", 6),
        ]);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(request));
    }

    [Fact]
    public async Task Update_RejectsMissingOrDuplicateTier()
    {
        var service = new RankingSlotQuotaService(_db);
        var request = new UpdateRankingSlotQuotasDto(_departmentId,
        [
            new RankingSlotQuotaDto("A1", 2),
            new RankingSlotQuotaDto("A1", 3),
            new RankingSlotQuotaDto("A3", 4),
            new RankingSlotQuotaDto("B", 6),
        ]);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(request));
    }

    [Fact]
    public async Task Update_RejectsUnknownDepartment()
    {
        var service = new RankingSlotQuotaService(_db);
        var request = new UpdateRankingSlotQuotasDto(Guid.NewGuid(),
        [
            new RankingSlotQuotaDto("A1", 2),
            new RankingSlotQuotaDto("A2", 3),
            new RankingSlotQuotaDto("A3", 4),
            new RankingSlotQuotaDto("B", 6),
            new RankingSlotQuotaDto("C", 5),
        ]);

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(request));
    }

    [Fact]
    public async Task Get_RejectsUnknownDepartment()
    {
        var service = new RankingSlotQuotaService(_db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    private UpdateRankingSlotQuotasDto Request(int a1, int a2, int a3, int b, int c = 5) => new(_departmentId,
    [
        new RankingSlotQuotaDto("A1", a1),
        new RankingSlotQuotaDto("A2", a2),
        new RankingSlotQuotaDto("A3", a3),
        new RankingSlotQuotaDto("B", b),
        new RankingSlotQuotaDto("C", c),
    ]);
}
