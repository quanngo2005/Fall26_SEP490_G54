using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class KpiDefinitionService(AppDbContext db) : IKpiDefinitionService
{
    public async Task<PagedResult<KpiDefinitionDto>> GetKpisAsync(
        KpiFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var status = EnumWireFormat.ParseWire<KpiStatus>(filter.Status, "status");
        var aggregationType = EnumWireFormat.ParseWire<KpiAggregationType>(filter.AggregationType, "aggregationType");
        var query = db.KpiDefinitions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(kpi =>
                kpi.Code.Contains(keyword) ||
                kpi.Name.Contains(keyword) ||
                (kpi.Description != null && kpi.Description.Contains(keyword)));
        }

        if (status.HasValue)
        {
            query = query.Where(kpi => kpi.Status == status.Value);
        }

        if (filter.KpiGroupId.HasValue)
        {
            query = query.Where(kpi => kpi.KpiGroupId == filter.KpiGroupId.Value);
        }

        if (aggregationType.HasValue)
        {
            query = query.Where(kpi => kpi.AggregationType == aggregationType.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(kpi => kpi.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(kpi => new KpiDefinitionDto(
                kpi.Id,
                kpi.KpiGroupId,
                kpi.KpiGroup.Name,
                kpi.Code,
                kpi.Name,
                kpi.Description,
                kpi.Formula,
                kpi.Unit,
                kpi.AggregationType.ToWire(),
                kpi.Direction.ToWire(),
                kpi.MaxAchievementPercent,
                kpi.Status.ToWire(),
                kpi.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<KpiDefinitionDto>(items, totalCount, page, pageSize);
    }

    public async Task<KpiDefinitionDto?> GetKpiByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.KpiDefinitions
            .AsNoTracking()
            .Where(kpi => kpi.Id == id)
            .Select(kpi => new KpiDefinitionDto(
                kpi.Id,
                kpi.KpiGroupId,
                kpi.KpiGroup.Name,
                kpi.Code,
                kpi.Name,
                kpi.Description,
                kpi.Formula,
                kpi.Unit,
                kpi.AggregationType.ToWire(),
                kpi.Direction.ToWire(),
                kpi.MaxAchievementPercent,
                kpi.Status.ToWire(),
                kpi.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<List<KpiGroupOptionDto>> GetGroupOptionsAsync(CancellationToken cancellationToken = default)
    {
        return db.KpiGroups
            .AsNoTracking()
            .Where(group => group.Status == KpiStatus.Active)
            .OrderBy(group => group.Code)
            .Select(group => new KpiGroupOptionDto(group.Id, group.Code, group.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<KpiDefinitionDto> CreateKpiAsync(
        KpiDefinitionSaveDto dto,
        CancellationToken cancellationToken = default)
    {
        var (aggregationType, direction) = Validate(dto);
        var code = dto.Code.Trim();
        await EnsureCodeUniqueAsync(code, null, cancellationToken);
        await EnsureGroupExistsAsync(dto.KpiGroupId, cancellationToken);

        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = dto.KpiGroupId,
            Code = code,
            Name = dto.Name.Trim(),
            Description = NormalizeOptional(dto.Description),
            Formula = NormalizeOptional(dto.Formula),
            Unit = dto.Unit.Trim(),
            AggregationType = aggregationType,
            Direction = direction,
            MaxAchievementPercent = dto.MaxAchievementPercent,
            Status = KpiStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.KpiDefinitions.Add(kpi);
        await db.SaveChangesAsync(cancellationToken);
        return await GetRequiredKpiAsync(kpi.Id, cancellationToken);
    }

    public async Task<KpiDefinitionDto> UpdateKpiAsync(
        Guid id,
        KpiDefinitionSaveDto dto,
        CancellationToken cancellationToken = default)
    {
        var (aggregationType, direction) = Validate(dto);
        var kpi = await db.KpiDefinitions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("KPI Definition", id);

        var code = dto.Code.Trim();
        await EnsureCodeUniqueAsync(code, id, cancellationToken);
        await EnsureGroupExistsAsync(dto.KpiGroupId, cancellationToken);

        kpi.KpiGroupId = dto.KpiGroupId;
        kpi.Code = code;
        kpi.Name = dto.Name.Trim();
        kpi.Description = NormalizeOptional(dto.Description);
        kpi.Formula = NormalizeOptional(dto.Formula);
        kpi.Unit = dto.Unit.Trim();
        kpi.AggregationType = aggregationType;
        kpi.Direction = direction;
        kpi.MaxAchievementPercent = dto.MaxAchievementPercent;
        kpi.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return await GetRequiredKpiAsync(id, cancellationToken);
    }

    public async Task ChangeStatusAsync(
        Guid id,
        KpiStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var status = EnumWireFormat.ParseRequiredWire<KpiStatus>(dto.Status, "status");

        var kpi = await db.KpiDefinitions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("KPI Definition", id);

        kpi.Status = status;
        kpi.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await db.KpiDefinitions.AnyAsync(
            kpi => kpi.Code == code && (!excludeId.HasValue || kpi.Id != excludeId.Value),
            cancellationToken);
        if (exists)
        {
            throw new ConflictException($"Mã KPI '{code}' đã tồn tại.");
        }
    }

    private async Task EnsureGroupExistsAsync(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await db.KpiGroups.AnyAsync(group => group.Id == groupId, cancellationToken))
        {
            throw new NotFoundException("KPI Group", groupId);
        }
    }

    private async Task<KpiDefinitionDto> GetRequiredKpiAsync(Guid id, CancellationToken cancellationToken)
    {
        return await GetKpiByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("KPI Definition", id);
    }

    private static (KpiAggregationType AggregationType, KpiDirection Direction) Validate(KpiDefinitionSaveDto dto)
    {
        if (dto.KpiGroupId == Guid.Empty || string.IsNullOrWhiteSpace(dto.Code) || dto.Code.Trim().Length > 50 ||
            string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 255 ||
            string.IsNullOrWhiteSpace(dto.Unit) || dto.Unit.Trim().Length > 50)
        {
            throw new ValidationException("Nhóm KPI, mã, tên và đơn vị là bắt buộc; mã tối đa 50, tên 255 và đơn vị 50 ký tự.");
        }

        var aggregationType = EnumWireFormat.ParseRequiredWire<KpiAggregationType>(dto.AggregationType, "aggregationType");
        var direction = EnumWireFormat.ParseRequiredWire<KpiDirection>(dto.Direction, "direction");

        if (dto.MaxAchievementPercent is <= 0)
        {
            throw new ValidationException("Mức trần phần trăm hoàn thành phải lớn hơn 0.");
        }

        return (aggregationType, direction);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
