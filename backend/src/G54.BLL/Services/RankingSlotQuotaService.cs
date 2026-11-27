using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameter;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class RankingSlotQuotaService(AppDbContext db, SystemParameterStore? parameters = null) : IRankingSlotQuotaService
{
    public async Task<RankingSlotQuotasDto> GetAsync(Guid departmentId, Guid? periodId = null, CancellationToken ct = default)
    {
        await EnsureDepartmentExistsAsync(departmentId, ct);

        Dictionary<string, int> rows;

        if (periodId.HasValue)
        {
            var period = await db.EvaluationPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId.Value, ct);

            // 1. Direct match for period
            rows = await db.RankingSlotQuotas.AsNoTracking()
                .Where(q => q.DepartmentId == departmentId && q.EvaluationPeriodId == periodId.Value && !q.IsDeleted)
                .ToDictionaryAsync(q => q.TierCode, q => q.Headcount, ct);

            // 2. Fallback to latest preceding period
            if (rows.Count == 0 && period is not null)
            {
                var prevPeriodId = await db.RankingSlotQuotas.AsNoTracking()
                    .Where(q => q.DepartmentId == departmentId && !q.IsDeleted && q.EvaluationPeriodId != null && q.EvaluationPeriod!.StartDate < period.StartDate)
                    .OrderByDescending(q => q.EvaluationPeriod!.StartDate)
                    .Select(q => q.EvaluationPeriodId)
                    .FirstOrDefaultAsync(ct);

                if (prevPeriodId.HasValue)
                {
                    rows = await db.RankingSlotQuotas.AsNoTracking()
                        .Where(q => q.DepartmentId == departmentId && q.EvaluationPeriodId == prevPeriodId.Value && !q.IsDeleted)
                        .ToDictionaryAsync(q => q.TierCode, q => q.Headcount, ct);
                }
            }

            // 3. Fallback to baseline
            if (rows.Count == 0)
            {
                rows = await db.RankingSlotQuotas.AsNoTracking()
                    .Where(q => q.DepartmentId == departmentId && q.EvaluationPeriodId == null && !q.IsDeleted)
                    .ToDictionaryAsync(q => q.TierCode, q => q.Headcount, ct);
            }
        }
        else
        {
            rows = await db.RankingSlotQuotas.AsNoTracking()
                .Where(q => q.DepartmentId == departmentId && q.EvaluationPeriodId == null && !q.IsDeleted)
                .ToDictionaryAsync(q => q.TierCode, q => q.Headcount, ct);
        }

        var slots = RankingSlotTierCodes.All
            .Select(code => new RankingSlotQuotaDto(code, rows.TryGetValue(code, out var headcount) ? headcount : null))
            .ToList();

        return new RankingSlotQuotasDto(departmentId, slots, periodId);
    }

    public async Task<RankingSlotQuotasDto> UpdateAsync(UpdateRankingSlotQuotasDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        var codes = request.Slots.Select(s => s.TierCode).ToList();
        if (codes.Count != RankingSlotTierCodes.All.Count
            || RankingSlotTierCodes.All.Any(code => codes.Count(c => c == code) != 1))
        {
            errors.Add("Phải nhập đúng một slot cho mỗi hạng A1, A2, A3, B và C.");
        }

        if (request.Slots.Any(s => s.Headcount is null or < 0))
        {
            errors.Add("Số lượng slot phải là số nguyên không âm.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await EnsureDepartmentExistsAsync(request.DepartmentId, ct);

        if (request.PeriodId.HasValue)
        {
            var period = await db.EvaluationPeriods.FirstOrDefaultAsync(p => p.Id == request.PeriodId.Value, ct)
                ?? throw new NotFoundException("Kỳ đánh giá", request.PeriodId.Value);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed || period.EndDate < today)
            {
                throw new BusinessRuleException(
                    ErrorCodes.PeriodLocked,
                    "Không thể cập nhật hạn ngạch quota cho kỳ đánh giá đã khóa hoặc đã qua.",
                    StatusCodes.Status409Conflict);
            }

            var existing = await db.RankingSlotQuotas
                .Where(q => q.DepartmentId == request.DepartmentId && q.EvaluationPeriodId == request.PeriodId.Value)
                .ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;

            foreach (var slot in request.Slots)
            {
                var row = existing.FirstOrDefault(q => q.TierCode == slot.TierCode);
                if (row is null)
                {
                    db.RankingSlotQuotas.Add(new RankingSlotQuota
                    {
                        DepartmentId = request.DepartmentId,
                        EvaluationPeriodId = request.PeriodId.Value,
                        TierCode = slot.TierCode,
                        Headcount = slot.Headcount!.Value,
                        UpdatedAt = now,
                    });
                    continue;
                }

                row.Headcount = slot.Headcount!.Value;
                row.IsDeleted = false;
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return await GetAsync(request.DepartmentId, request.PeriodId, ct);
        }
        else
        {
            var existing = await db.RankingSlotQuotas
                .Where(q => q.DepartmentId == request.DepartmentId && q.EvaluationPeriodId == null)
                .ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;

            foreach (var slot in request.Slots)
            {
                var row = existing.FirstOrDefault(q => q.TierCode == slot.TierCode);
                if (row is null)
                {
                    db.RankingSlotQuotas.Add(new RankingSlotQuota
                    {
                        DepartmentId = request.DepartmentId,
                        EvaluationPeriodId = null,
                        TierCode = slot.TierCode,
                        Headcount = slot.Headcount!.Value,
                        UpdatedAt = now,
                    });
                    continue;
                }

                row.Headcount = slot.Headcount!.Value;
                row.IsDeleted = false;
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return await GetAsync(request.DepartmentId, null, ct);
        }
    }

    public async Task<RankingSlotQuotasDto> GetBranchQuotaAsync(Guid periodId, CancellationToken ct = default)
    {
        var branch = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.LevelType == DepartmentLevelType.Branch && d.Status == RecordStatus.Active, ct)
            ?? await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.LevelType == DepartmentLevelType.Branch, ct)
            ?? throw new NotFoundException("Không tìm thấy chi nhánh đang hoạt động.");

        return await GetAsync(branch.Id, periodId, ct);
    }

    public async Task<RankingSlotQuotasDto> UpdateBranchQuotaAsync(Guid periodId, List<RankingSlotQuotaDto> slots, CancellationToken ct = default)
    {
        var branch = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.LevelType == DepartmentLevelType.Branch && d.Status == RecordStatus.Active, ct)
            ?? await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.LevelType == DepartmentLevelType.Branch, ct)
            ?? throw new NotFoundException("Không tìm thấy chi nhánh đang hoạt động.");

        var activeEmployeeCount = await db.Employees
            .AsNoTracking()
            .CountAsync(e => e.Status == EmployeeStatus.Active, ct);

        if (activeEmployeeCount > 0 && slots is { Count: > 0 })
        {
            var opts = parameters?.Current ?? new SystemParametersOptions();
            var maxTierA = Math.Max(1, (int)Math.Ceiling(activeEmployeeCount * (opts.TierAQuotaPercent / 100m)));
            var maxTierB = (int)Math.Ceiling(activeEmployeeCount * (opts.TierBTargetPercent / 100m));
            var maxTierC = (int)Math.Ceiling(activeEmployeeCount * (opts.TierCTargetPercent / 100m));

            var sumA = slots.Where(s => s.TierCode.StartsWith('A')).Sum(s => s.Headcount ?? 0);
            var slotB = slots.FirstOrDefault(s => s.TierCode == "B")?.Headcount ?? 0;
            var slotC = slots.FirstOrDefault(s => s.TierCode == "C")?.Headcount ?? 0;
            var totalSlots = sumA + slotB + slotC;

            var ratioErrors = new List<string>();
            if (sumA > maxTierA)
            {
                ratioErrors.Add($"Tổng quota hạng A ({sumA}) vượt quá tỉ lệ cho phép {opts.TierAQuotaPercent:0.#}% ({maxTierA} slot).");
            }
            if (slotB > maxTierB)
            {
                ratioErrors.Add($"Quota hạng B ({slotB}) vượt quá tỉ lệ cho phép {opts.TierBTargetPercent:0.#}% ({maxTierB} slot).");
            }
            if (slotC > maxTierC)
            {
                ratioErrors.Add($"Quota hạng C ({slotC}) vượt quá tỉ lệ cho phép {opts.TierCTargetPercent:0.#}% ({maxTierC} slot).");
            }
            if (totalSlots > activeEmployeeCount)
            {
                ratioErrors.Add($"Tổng quota A/B/C ({totalSlots}) vượt quá tổng nhân viên chi nhánh ({activeEmployeeCount}).");
            }

            if (ratioErrors.Count > 0)
            {
                throw new ValidationException(ratioErrors);
            }
        }

        return await UpdateAsync(new UpdateRankingSlotQuotasDto(branch.Id, slots, periodId), ct);
    }

    private async Task EnsureDepartmentExistsAsync(Guid departmentId, CancellationToken ct)
    {
        var exists = await db.Departments.AsNoTracking().AnyAsync(d => d.Id == departmentId, ct);
        if (!exists)
        {
            throw new NotFoundException("Phòng ban", departmentId);
        }
    }
}
