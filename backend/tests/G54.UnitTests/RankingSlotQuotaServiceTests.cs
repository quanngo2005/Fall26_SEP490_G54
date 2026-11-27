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
        _db.Departments.Add(new Department { Id = _departmentId, Code = "DEP-SLOT", Name = "Phòng slot", LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active });
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

    [Fact]
    public async Task Update_WhenPeriodIsLockedOrClosed_ThrowsBusinessRuleExceptionWith409()
    {
        var lockedPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "PER-LOCKED",
            Name = "Kỳ khóa",
            Status = PeriodStatus.Locked,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        _db.EvaluationPeriods.Add(lockedPeriod);
        await _db.SaveChangesAsync();

        var service = new RankingSlotQuotaService(_db);
        var request = Request(1, 2, 3, 4, 5) with { PeriodId = lockedPeriod.Id };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(request));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Update_WhenPeriodIsInThePast_ThrowsBusinessRuleExceptionWith409()
    {
        var pastPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "PER-PAST",
            Name = "Kỳ quá khứ",
            Status = PeriodStatus.Open,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        };
        _db.EvaluationPeriods.Add(pastPeriod);
        await _db.SaveChangesAsync();

        var service = new RankingSlotQuotaService(_db);
        var request = Request(1, 2, 3, 4, 5) with { PeriodId = pastPeriod.Id };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(request));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Get_WhenPeriodHasNoDirectQuotas_FallsBackToPriorPeriod()
    {
        var period1 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "PER-1",
            Name = "Kỳ 1",
            Status = PeriodStatus.Closed,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 31),
        };
        var period2 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "PER-2",
            Name = "Kỳ 2",
            Status = PeriodStatus.Open,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
        };
        _db.EvaluationPeriods.AddRange(period1, period2);
        await _db.SaveChangesAsync();

        var service = new RankingSlotQuotaService(_db);
        await service.UpdateAsync(Request(10, 20, 30, 40, 50) with { PeriodId = null }); // baseline
        // Period 1 was saved directly in db (e.g. before closing)
        _db.RankingSlotQuotas.AddRange(
            new RankingSlotQuota { DepartmentId = _departmentId, EvaluationPeriodId = period1.Id, TierCode = "A1", Headcount = 1 },
            new RankingSlotQuota { DepartmentId = _departmentId, EvaluationPeriodId = period1.Id, TierCode = "A2", Headcount = 2 },
            new RankingSlotQuota { DepartmentId = _departmentId, EvaluationPeriodId = period1.Id, TierCode = "A3", Headcount = 3 },
            new RankingSlotQuota { DepartmentId = _departmentId, EvaluationPeriodId = period1.Id, TierCode = "B", Headcount = 4 },
            new RankingSlotQuota { DepartmentId = _departmentId, EvaluationPeriodId = period1.Id, TierCode = "C", Headcount = 5 });
        await _db.SaveChangesAsync();

        // Querying period2 without explicit quotas should fall back to period1 (the latest prior period)
        var result = await service.GetAsync(_departmentId, period2.Id);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, result.Slots.Select(s => s.Headcount).ToArray());

        // Updating period2 does not change period1
        await service.UpdateAsync(Request(7, 7, 7, 7, 7) with { PeriodId = period2.Id });

        var period1Quotas = await service.GetAsync(_departmentId, period1.Id);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, period1Quotas.Slots.Select(s => s.Headcount).ToArray());

        var period2Quotas = await service.GetAsync(_departmentId, period2.Id);
        Assert.Equal(new int?[] { 7, 7, 7, 7, 7 }, period2Quotas.Slots.Select(s => s.Headcount).ToArray());
    }

    [Fact]
    public async Task BranchQuota_GetsAndUpdatesCorrectly()
    {
        var branch = new Department { Id = Guid.NewGuid(), Code = "BR-01", Name = "Chi nhánh", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active };
        var period = new EvaluationPeriod { Id = Guid.NewGuid(), Code = "2026-P1", Name = "Kỳ 1", PeriodType = PeriodType.Month, StartDate = DateOnly.FromDateTime(DateTime.UtcNow), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), Status = PeriodStatus.Open };
        _db.Departments.Add(branch);
        _db.EvaluationPeriods.Add(period);
        await _db.SaveChangesAsync();

        var service = new RankingSlotQuotaService(_db);
        var slots = new List<RankingSlotQuotaDto>
        {
            new("A1", 2),
            new("A2", 3),
            new("A3", 4),
            new("B", 8),
            new("C", 3),
        };

        var updated = await service.UpdateBranchQuotaAsync(period.Id, slots);
        Assert.Equal(branch.Id, updated.DepartmentId);
        Assert.Equal(new int?[] { 2, 3, 4, 8, 3 }, updated.Slots.Select(s => s.Headcount).ToArray());

        var fetched = await service.GetBranchQuotaAsync(period.Id);
        Assert.Equal(new int?[] { 2, 3, 4, 8, 3 }, fetched.Slots.Select(s => s.Headcount).ToArray());
    }

    [Fact]
    public async Task UpdateBranchQuota_EnforcesTierABCRatioLimits()
    {
        var branch = new Department { Id = Guid.NewGuid(), Code = "BR-RATIO", Name = "Chi nhánh Ratio", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active };
        var pos = new JobPosition { Id = Guid.NewGuid(), Code = "POS-R", Title = "NV", Status = RecordStatus.Active };
        var period = new EvaluationPeriod { Id = Guid.NewGuid(), Code = "2026-11", Name = "Tháng 11/2026", StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 30), Status = PeriodStatus.Open };
        _db.Departments.Add(branch);
        _db.JobPositions.Add(pos);
        _db.EvaluationPeriods.Add(period);
        for (var i = 0; i < 10; i++)
        {
            _db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = $"EMP-{i:00}",
                FullName = $"Nhân viên {i}",
                Email = $"emp{i}@test.local",
                DepartmentId = _departmentId,
                JobPositionId = pos.Id,
                Status = EmployeeStatus.Active,
            });
        }
        await _db.SaveChangesAsync();

        var service = new RankingSlotQuotaService(_db);
        // 10 employees -> defaults: A=20% (2), B=60% (6), C=15% (2)
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateBranchQuotaAsync(period.Id,
        [
            new("A1", 1), new("A2", 1), new("A3", 0), new("B", 7), new("C", 1),
        ]));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateBranchQuotaAsync(period.Id,
        [
            new("A1", 1), new("A2", 1), new("A3", 0), new("B", 5), new("C", 3),
        ]));

        var valid = await service.UpdateBranchQuotaAsync(period.Id,
        [
            new("A1", 1), new("A2", 1), new("A3", 0), new("B", 6), new("C", 2),
        ]);
        Assert.Equal(new int?[] { 1, 1, 0, 6, 2 }, valid.Slots.Select(s => s.Headcount).ToArray());
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
