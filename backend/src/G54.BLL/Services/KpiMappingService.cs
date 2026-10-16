using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.KpiDefinition;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class KpiMappingService(AppDbContext db) : IKpiMappingService
{
    public async Task<List<ObjectiveKpiDto>> GetObjectiveKpisAsync(
        Guid objectiveId,
        CancellationToken cancellationToken = default)
    {
        await EnsureObjectiveExistsAsync(objectiveId, cancellationToken);

        return await db.ObjectiveKpis
            .AsNoTracking()
            .Where(mapping => mapping.BusinessObjectiveId == objectiveId)
            .OrderBy(mapping => mapping.KpiDefinition.Code)
            .Select(mapping => new ObjectiveKpiDto(
                mapping.Id,
                mapping.BusinessObjectiveId,
                mapping.KpiDefinitionId,
                mapping.KpiDefinition.Code,
                mapping.KpiDefinition.Name,
                mapping.KpiDefinition.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<ObjectiveKpiDto> AddObjectiveKpiAsync(
        Guid objectiveId,
        ObjectiveKpiSaveDto dto,
        CancellationToken cancellationToken = default)
    {
        await EnsureObjectiveExistsAsync(objectiveId, cancellationToken);
        if (dto.KpiDefinitionId == Guid.Empty)
        {
            throw new ValidationException("KpiDefinitionId là bắt buộc.");
        }

        var kpi = await db.KpiDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == dto.KpiDefinitionId, cancellationToken)
            ?? throw new NotFoundException("KPI Definition", dto.KpiDefinitionId);

        if (kpi.Status != KpiStatus.Active)
        {
            throw new BusinessRuleException(ErrorCodes.ValidationError, "Chỉ KPI đang hoạt động mới được liên kết với mục tiêu.");
        }

        if (await db.ObjectiveKpis.AnyAsync(
                mapping => mapping.BusinessObjectiveId == objectiveId && mapping.KpiDefinitionId == dto.KpiDefinitionId,
                cancellationToken))
        {
            throw new ConflictException("KPI đã được liên kết với mục tiêu này.");
        }

        var mapping = new ObjectiveKpi
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = objectiveId,
            KpiDefinitionId = dto.KpiDefinitionId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.ObjectiveKpis.Add(mapping);
        await db.SaveChangesAsync(cancellationToken);

        return new ObjectiveKpiDto(mapping.Id, objectiveId, kpi.Id, kpi.Code, kpi.Name, kpi.Unit);
    }

    public async Task RemoveObjectiveKpiAsync(
        Guid objectiveId,
        Guid kpiId,
        CancellationToken cancellationToken = default)
    {
        await EnsureObjectiveExistsAsync(objectiveId, cancellationToken);
        var mapping = await db.ObjectiveKpis.SingleOrDefaultAsync(
            item => item.BusinessObjectiveId == objectiveId && item.KpiDefinitionId == kpiId,
            cancellationToken);
        if (mapping is null)
        {
            throw new NotFoundException("Objective KPI mapping", kpiId);
        }

        db.ObjectiveKpis.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<List<KpiOptionDto>> GetAvailableKpisAsync(CancellationToken cancellationToken = default)
    {
        return db.KpiDefinitions
            .AsNoTracking()
            .Where(kpi => kpi.Status == KpiStatus.Active)
            .OrderBy(kpi => kpi.Code)
            .Select(kpi => new KpiOptionDto(kpi.Id, kpi.Code, kpi.Name, kpi.Unit))
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureObjectiveExistsAsync(Guid objectiveId, CancellationToken cancellationToken)
    {
        if (!await db.BusinessObjectives.AnyAsync(item => item.Id == objectiveId, cancellationToken))
        {
            throw new NotFoundException("Business Objective", objectiveId);
        }
    }
}
