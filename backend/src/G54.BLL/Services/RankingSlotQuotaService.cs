using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameter;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class RankingSlotQuotaService(AppDbContext db) : IRankingSlotQuotaService
{
    public async Task<RankingSlotQuotasDto> GetAsync(Guid departmentId, CancellationToken ct = default)
    {
        await EnsureDepartmentExistsAsync(departmentId, ct);

        var rows = await db.RankingSlotQuotas.AsNoTracking()
            .Where(q => q.DepartmentId == departmentId && !q.IsDeleted)
            .ToDictionaryAsync(q => q.TierCode, q => q.Headcount, ct);

        var slots = RankingSlotTierCodes.All
            .Select(code => new RankingSlotQuotaDto(code, rows.TryGetValue(code, out var headcount) ? headcount : null))
            .ToList();

        return new RankingSlotQuotasDto(departmentId, slots);
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

        var existing = await db.RankingSlotQuotas
            .Where(q => q.DepartmentId == request.DepartmentId)
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

        return await GetAsync(request.DepartmentId, ct);
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
