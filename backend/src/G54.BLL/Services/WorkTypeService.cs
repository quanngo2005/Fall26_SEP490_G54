using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.WorkType;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class WorkTypeService(AppDbContext db, ILogger<WorkTypeService> logger) : IWorkTypeService
{
    private static readonly Action<ILogger, string, Guid, Exception?> LogWorkTypeAudit =
        LoggerMessage.Define<string, Guid>(LogLevel.Information, new EventId(1, "WorkTypeAudit"), "WorkType audit: Action={Action}, EntityId={EntityId}");

    public async Task<List<KpiOptionDto>> GetActiveKpisAsync(CancellationToken cancellationToken = default)
    {
        return await db.KpiDefinitions
            .AsNoTracking()
            .Where(k => k.Status == KpiStatus.Active)
            .OrderBy(k => k.Code)
            .Select(k => new KpiOptionDto(k.Id, k.Code, k.Name, k.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<WorkflowOptionDto>> GetWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        return await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(w => w.Status == WorkflowDefinitionStatus.Active)
            .OrderBy(w => w.Code)
            .Select(w => new WorkflowOptionDto(w.Id, w.Code, w.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkTypeDetailDto> CreateWorkTypeAsync(
        CreateWorkTypeDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(dto.Code, dto.Name, dto.WorkflowDefinitionId);
        ValidateKpiDateRanges(dto.Kpis);

        var code = dto.Code.Trim();
        await EnsureCodeUniqueAsync(code, null, cancellationToken);
        await EnsureWorkflowExistsAsync(dto.WorkflowDefinitionId, cancellationToken);

        var kpiIds = dto.Kpis.Select(k => k.KpiDefinitionId).Distinct().ToList();
        if (kpiIds.Count > 0)
        {
            await EnsureKpisActiveAsync(kpiIds, cancellationToken);
        }

        var workType = new WorkType
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = dto.Name.Trim(),
            Description = NormalizeOptional(dto.Description),
            WorkflowDefinitionId = dto.WorkflowDefinitionId,
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var kpiItem in dto.Kpis)
        {
            workType.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = workType.Id,
                KpiDefinitionId = kpiItem.KpiDefinitionId,
                IsRequired = kpiItem.IsRequired,
                EffectiveFrom = kpiItem.EffectiveFrom,
                EffectiveTo = kpiItem.EffectiveTo,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        db.WorkTypes.Add(workType);
        await db.SaveChangesAsync(cancellationToken);

        await RecordAuditLogAsync("CREATE_WORK_TYPE", workType.Id);
        return await GetRequiredWorkTypeAsync(workType.Id, cancellationToken);
    }

    public async Task<PagedResult<WorkTypeDetailDto>> GetWorkTypesAsync(
        WorkTypeFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var status = EnumApiFormat.ParseEnum<RecordStatus>(filter.Status, "status");
        var query = db.WorkTypes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(w =>
                w.Code.Contains(keyword) ||
                w.Name.Contains(keyword) ||
                (w.Description != null && w.Description.Contains(keyword)));
        }

        if (status.HasValue)
        {
            var recordStatus = status.Value;
            query = query.Where(w => w.Status == recordStatus);
        }

        if (filter.WorkflowDefinitionId.HasValue)
        {
            query = query.Where(w => w.WorkflowDefinitionId == filter.WorkflowDefinitionId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(w => w.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new WorkTypeDetailDto(
                w.Id,
                w.Code,
                w.Name,
                w.Description,
                w.WorkflowDefinitionId,
                w.WorkflowDefinition.Name,
                w.Status.ToEnumString(),
                w.UpdatedAt,
                w.WorkTypeKpis
                    .OrderBy(k => k.KpiDefinition.Code)
                    .Select(k => new WorkTypeKpiDetailDto(
                        k.KpiDefinitionId,
                        k.KpiDefinition.Code,
                        k.KpiDefinition.Name,
                        k.IsRequired,
                        k.EffectiveFrom,
                        k.EffectiveTo))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new PagedResult<WorkTypeDetailDto>(items, totalCount, page, pageSize);
    }

    public async Task<WorkTypeDetailDto?> GetWorkTypeByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await db.WorkTypes
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new WorkTypeDetailDto(
                w.Id,
                w.Code,
                w.Name,
                w.Description,
                w.WorkflowDefinitionId,
                w.WorkflowDefinition.Name,
                w.Status.ToEnumString(),
                w.UpdatedAt,
                w.WorkTypeKpis
                    .OrderBy(k => k.KpiDefinition.Code)
                    .Select(k => new WorkTypeKpiDetailDto(
                        k.KpiDefinitionId,
                        k.KpiDefinition.Code,
                        k.KpiDefinition.Name,
                        k.IsRequired,
                        k.EffectiveFrom,
                        k.EffectiveTo))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<WorkTypeDetailDto> UpdateWorkTypeAsync(
        Guid id,
        UpdateWorkTypeDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(dto.Code, dto.Name, dto.WorkflowDefinitionId);
        ValidateKpiDateRanges(dto.Kpis);

        var workType = await db.WorkTypes
            .Include(w => w.WorkTypeKpis)
            .SingleOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException("WorkType", id);

        var code = dto.Code.Trim();
        await EnsureCodeUniqueAsync(code, id, cancellationToken);
        await EnsureWorkflowExistsAsync(dto.WorkflowDefinitionId, cancellationToken);

        var kpiIds = dto.Kpis.Select(k => k.KpiDefinitionId).Distinct().ToList();
        if (kpiIds.Count > 0)
        {
            await EnsureKpisActiveAsync(kpiIds, cancellationToken);
        }

        workType.Code = code;
        workType.Name = dto.Name.Trim();
        workType.Description = NormalizeOptional(dto.Description);
        workType.WorkflowDefinitionId = dto.WorkflowDefinitionId;
        workType.UpdatedAt = DateTimeOffset.UtcNow;

        db.WorkTypeKpis.RemoveRange(workType.WorkTypeKpis);
        foreach (var kpiItem in dto.Kpis)
        {
            var newKpi = new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = workType.Id,
                KpiDefinitionId = kpiItem.KpiDefinitionId,
                IsRequired = kpiItem.IsRequired,
                EffectiveFrom = kpiItem.EffectiveFrom,
                EffectiveTo = kpiItem.EffectiveTo,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.WorkTypeKpis.Add(newKpi);
        }

        await db.SaveChangesAsync(cancellationToken);
        await RecordAuditLogAsync("UPDATE_WORK_TYPE", id);

        return await GetRequiredWorkTypeAsync(id, cancellationToken);
    }

    public async Task ChangeStatusAsync(
        Guid id,
        WorkTypeStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var status = EnumApiFormat.ParseRequiredEnum<RecordStatus>(dto.Status, "status");

        var workType = await db.WorkTypes.SingleOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException("WorkType", id);

        workType.Status = status;
        workType.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await RecordAuditLogAsync("CHANGE_WORK_TYPE_STATUS", id);
    }

    public async Task DeleteWorkTypeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var workType = await db.WorkTypes
            .Include(w => w.WorkTypeKpis)
            .SingleOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException("WorkType", id);

        var hasWorks = await db.Works.AnyAsync(w => w.WorkTypeId == id, cancellationToken);
        if (hasWorks)
        {
            throw new ConflictException("Không thể xóa loại công việc đã phát sinh công việc thực tế. Vui lòng chuyển trạng thái sang Lưu trữ (Archived).");
        }

        db.WorkTypeKpis.RemoveRange(workType.WorkTypeKpis);
        db.WorkTypes.Remove(workType);
        await db.SaveChangesAsync(cancellationToken);

        await RecordAuditLogAsync("DELETE_WORK_TYPE", id);
    }

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await db.WorkTypes.AnyAsync(
            w => w.Code == code && (!excludeId.HasValue || w.Id != excludeId.Value),
            cancellationToken);
        if (exists)
        {
            throw new ConflictException($"Mã loại công việc '{code}' đã tồn tại.");
        }
    }

    private async Task EnsureWorkflowExistsAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var exists = await db.WorkflowDefinitions.AnyAsync(
            w => w.Id == workflowId && w.Status == WorkflowDefinitionStatus.Active,
            cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Workflow Definition", workflowId);
        }
    }

    private async Task EnsureKpisActiveAsync(List<Guid> kpiIds, CancellationToken cancellationToken)
    {
        var activeCount = await db.KpiDefinitions.CountAsync(
            k => kpiIds.Contains(k.Id) && k.Status == KpiStatus.Active,
            cancellationToken);

        if (activeCount != kpiIds.Count)
        {
            throw new BusinessRuleException(
                ErrorCodes.ValidationError,
                "Một hoặc nhiều KPI được chọn không tồn tại hoặc không ở trạng thái hoạt động (Active).");
        }
    }

    private static void ValidateKpiDateRanges(List<WorkTypeKpiItemDto> kpis)
    {
        if (kpis is null || kpis.Count == 0)
        {
            return;
        }

        var duplicateKpi = kpis
            .GroupBy(k => k.KpiDefinitionId)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateKpi is not null)
        {
            throw new ValidationException($"Danh sách KPI cấu hình chứa KPI trùng lặp (ID: {duplicateKpi.Key}).");
        }

        foreach (var kpi in kpis)
        {
            if (kpi.KpiDefinitionId == Guid.Empty)
            {
                throw new ValidationException("KpiDefinitionId không được để trống.");
            }

            if (kpi.EffectiveTo.HasValue && kpi.EffectiveTo.Value < kpi.EffectiveFrom)
            {
                throw new ValidationException($"Ngày kết thúc ({kpi.EffectiveTo:yyyy-MM-dd}) không được nhỏ hơn ngày bắt đầu ({kpi.EffectiveFrom:yyyy-MM-dd}).");
            }
        }
    }

    private Task RecordAuditLogAsync(string action, Guid entityId)
    {
        LogWorkTypeAudit(logger, action, entityId, null);
        return Task.CompletedTask;
    }

    private async Task<WorkTypeDetailDto> GetRequiredWorkTypeAsync(Guid id, CancellationToken cancellationToken)
    {
        return await GetWorkTypeByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("WorkType", id);
    }

    private static void ValidateInput(string code, string name, Guid workflowDefinitionId)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
        {
            throw new ValidationException("Mã loại công việc là bắt buộc và tối đa 50 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ValidationException("Tên loại công việc là bắt buộc và tối đa 200 ký tự.");
        }

        if (workflowDefinitionId == Guid.Empty)
        {
            throw new ValidationException("Quy trình phê duyệt (WorkflowDefinitionId) là bắt buộc.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
