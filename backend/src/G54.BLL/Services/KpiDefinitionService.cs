using System.Linq.Expressions;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.KpiDefinition;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class KpiDefinitionService(AppDbContext db) : IKpiDefinitionService
{
    /// <summary>
    /// Transitions open to the status endpoint. A pending KPI leaves that state only through approve or reject, so it
    /// is never activated by a plain status change.
    /// </summary>
    private static readonly Dictionary<KpiStatus, KpiStatus[]> AllowedTransitions = new()
    {
        [KpiStatus.Draft] = [KpiStatus.PendingApproval, KpiStatus.Archived],
        [KpiStatus.PendingApproval] = [KpiStatus.Archived],
        [KpiStatus.Rejected] = [KpiStatus.PendingApproval, KpiStatus.Archived],
        [KpiStatus.Active] = [KpiStatus.Inactive, KpiStatus.Archived],
        [KpiStatus.Inactive] = [KpiStatus.Active, KpiStatus.Archived],
        [KpiStatus.Archived] = [],
    };

    private static readonly Expression<Func<KpiDefinition, KpiDefinitionDto>> ToDto = kpi => new KpiDefinitionDto(
        kpi.Id,
        kpi.KpiGroupId,
        kpi.KpiGroup.Name,
        kpi.Code,
        kpi.Name,
        kpi.Description,
        kpi.Formula,
        kpi.Unit,
        kpi.AggregationType.ToEnumString(),
        kpi.Direction.ToEnumString(),
        kpi.MaxAchievementPercent,
        kpi.Status.ToEnumString(),
        kpi.UpdatedAt,
        kpi.RejectReason,
        kpi.RequestNote,
        kpi.RequestedBy);

    public async Task<PagedResult<KpiDefinitionDto>> GetKpisAsync(
        KpiFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var status = EnumApiFormat.ParseEnum<KpiStatus>(filter.Status, "status");
        var aggregationType = EnumApiFormat.ParseEnum<KpiAggregationType>(filter.AggregationType, "aggregationType");
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
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return new PagedResult<KpiDefinitionDto>(items, totalCount, page, pageSize);
    }

    public async Task<KpiDefinitionDto?> GetKpiByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.KpiDefinitions
            .AsNoTracking()
            .Where(kpi => kpi.Id == id)
            .Select(ToDto)
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

    public Task<KpiDefinitionDto> CreateKpiAsync(
        KpiDefinitionSaveDto dto,
        CancellationToken cancellationToken = default) =>
        CreateDraftAsync(dto, null, null, cancellationToken);

    public async Task<KpiDefinitionDto> RequestKpiAsync(
        Guid accountId,
        KpiRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var requesterId = await ResolveEmployeeIdAsync(accountId, cancellationToken);

        var maxNote = db.MaxLength<KpiDefinition>(nameof(KpiDefinition.RequestNote));
        var note = dto.RequestNote?.Trim() ?? string.Empty;
        if (dto.Kpi is null || note.Length == 0 || note.Length > maxNote)
        {
            throw new ValidationException($"Thông tin KPI và lý do yêu cầu là bắt buộc; lý do tối đa {maxNote} ký tự.");
        }

        return await CreateDraftAsync(dto.Kpi, requesterId, note, cancellationToken);
    }

    public async Task<KpiDefinitionDto> UpdateKpiAsync(
        Guid id,
        KpiDefinitionSaveDto dto,
        CancellationToken cancellationToken = default)
    {
        var (aggregationType, direction) = Validate(dto);
        var kpi = await db.KpiDefinitions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("KPI Definition", id);

        if (kpi.Status == KpiStatus.PendingApproval)
        {
            // The approver must see what was submitted.
            throw new ConflictException("KPI đang chờ duyệt (Pending approval) nên không thể chỉnh sửa.");
        }

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

        await SaveAsync(code, cancellationToken);
        return await GetRequiredKpiAsync(id, cancellationToken);
    }

    public async Task ChangeStatusAsync(
        Guid accountId,
        Guid id,
        KpiStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var target = EnumApiFormat.ParseRequiredEnum<KpiStatus>(dto.Status, "status");

        var kpi = await db.KpiDefinitions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("KPI Definition", id);

        if (kpi.Status == target)
        {
            return;
        }

        if (!AllowedTransitions[kpi.Status].Contains(target))
        {
            throw new ConflictException(
                $"Không thể chuyển KPI từ {kpi.Status.ToEnumString()} sang {target.ToEnumString()}.");
        }

        if (target == KpiStatus.PendingApproval)
        {
            kpi.SubmittedBy = await ResolveEmployeeIdAsync(accountId, cancellationToken);
            kpi.RejectReason = null;
        }

        kpi.Status = target;
        kpi.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ApproveKpiAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default)
    {
        var approverId = await ResolveEmployeeIdAsync(accountId, cancellationToken);
        var kpi = await GetPendingKpiAsync(id, cancellationToken);
        EnsureApproverIsNotMaker(kpi, approverId);

        kpi.Status = KpiStatus.Active;
        kpi.RejectReason = null;
        kpi.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectKpiAsync(
        Guid accountId,
        Guid id,
        RejectKpiDto dto,
        CancellationToken cancellationToken = default)
    {
        var maxReason = db.MaxLength<KpiDefinition>(nameof(KpiDefinition.RejectReason));
        var reason = dto.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > maxReason)
        {
            throw new ValidationException($"Lý do từ chối là bắt buộc và tối đa {maxReason} ký tự.");
        }

        var approverId = await ResolveEmployeeIdAsync(accountId, cancellationToken);
        var kpi = await GetPendingKpiAsync(id, cancellationToken);
        EnsureApproverIsNotMaker(kpi, approverId);

        kpi.Status = KpiStatus.Rejected;
        kpi.RejectReason = reason;
        kpi.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<KpiDefinitionDto> CreateDraftAsync(
        KpiDefinitionSaveDto dto,
        Guid? requestedBy,
        string? requestNote,
        CancellationToken cancellationToken)
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
            RequestedBy = requestedBy,
            RequestNote = requestNote,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.KpiDefinitions.Add(kpi);
        await SaveAsync(code, cancellationToken);
        return await GetRequiredKpiAsync(kpi.Id, cancellationToken);
    }

    private async Task<KpiDefinition> GetPendingKpiAsync(Guid id, CancellationToken cancellationToken)
    {
        var kpi = await db.KpiDefinitions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("KPI Definition", id);

        if (kpi.Status != KpiStatus.PendingApproval)
        {
            throw new ConflictException(
                $"Chỉ KPI đang chờ duyệt mới có thể duyệt hoặc từ chối (hiện tại {kpi.Status.ToEnumString()}).");
        }

        return kpi;
    }

    /// <summary>The approver must be neither the one who sent the KPI for approval nor the one who requested it.</summary>
    private static void EnsureApproverIsNotMaker(KpiDefinition kpi, Guid approverId)
    {
        if (kpi.SubmittedBy == approverId || kpi.RequestedBy == approverId)
        {
            throw new BusinessRuleException(
                ErrorCodes.MakerCannotBeChecker,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private async Task<Guid> ResolveEmployeeIdAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var employeeId = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => (Guid?)a.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        return employeeId ?? throw new ForbiddenException();
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

    private async Task SaveAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
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

        var aggregationType = EnumApiFormat.ParseRequiredEnum<KpiAggregationType>(dto.AggregationType, "aggregationType");
        var direction = EnumApiFormat.ParseRequiredEnum<KpiDirection>(dto.Direction, "direction");

        if (dto.MaxAchievementPercent is <= 0)
        {
            throw new ValidationException("Mức trần phần trăm hoàn thành phải lớn hơn 0.");
        }

        return (aggregationType, direction);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
