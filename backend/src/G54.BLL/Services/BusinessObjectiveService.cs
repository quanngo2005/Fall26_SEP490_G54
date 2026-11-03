using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.BusinessObjective;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class BusinessObjectiveService(AppDbContext db, IPermissionService permissions, TimeProvider time)
    : IBusinessObjectiveService
{
    private readonly record struct ObjectiveInput(
        string Code,
        string Name,
        string? Description,
        DateOnly StartDate,
        DateOnly EndDate,
        decimal? PriorityWeight);

    public async Task<IReadOnlyList<BusinessObjectiveDto>> GetObjectivesAsync(
        Guid accountId,
        BusinessObjectiveFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var status = EnumApiFormat.ParseEnum<BusinessObjectiveStatus>(filter.Status, "status");
        var departments = await ResolveVisibleDepartmentsAsync(accountId, cancellationToken);

        var query = db.BusinessObjectives.AsNoTracking().AsQueryable();
        if (departments is not null)
        {
            query = query.Where(o => o.TargetAssignments.Any(t =>
                (t.DepartmentId != null && departments.Contains(t.DepartmentId.Value))
                || (t.Employee != null && departments.Contains(t.Employee.DepartmentId))));
        }

        if (status.HasValue)
        {
            var wanted = status.Value;
            query = query.Where(o => o.Status == wanted);
        }
        else
        {
            query = query.Where(o => o.Status != BusinessObjectiveStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(o => o.Code.Contains(keyword) || o.Name.Contains(keyword));
        }

        return await query
            .OrderBy(o => o.Code)
            .Select(o => new BusinessObjectiveDto(
                o.Id,
                o.ParentObjectiveId,
                o.Code,
                o.Name,
                o.Description,
                o.StartDate,
                o.EndDate,
                o.PriorityWeight,
                o.Status.ToEnumString(),
                o.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<BusinessObjectiveDetailDto> GetObjectiveAsync(
        Guid accountId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var departments = await ResolveVisibleDepartmentsAsync(accountId, cancellationToken);
        var objective = await db.BusinessObjectives.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Business Objective", id);

        var targets = db.TargetAssignments.AsNoTracking().Where(t => t.BusinessObjectiveId == id);
        if (departments is not null)
        {
            targets = targets.Where(t =>
                (t.DepartmentId != null && departments.Contains(t.DepartmentId.Value))
                || (t.Employee != null && departments.Contains(t.Employee.DepartmentId)));
            if (!await targets.AnyAsync(cancellationToken))
            {
                throw new ForbiddenException();
            }
        }

        var kpis = await db.ObjectiveKpis.AsNoTracking()
            .Where(m => m.BusinessObjectiveId == id)
            .OrderBy(m => m.KpiDefinition.Code)
            .Select(m => new ObjectiveKpiLinkDto(
                m.KpiDefinitionId,
                m.KpiDefinition.Code,
                m.KpiDefinition.Name,
                m.KpiDefinition.Unit))
            .ToListAsync(cancellationToken);

        var usage = new BusinessObjectiveUsageDto(
            kpis.Count,
            await targets.CountAsync(cancellationToken),
            await db.BusinessCampaigns.CountAsync(c => c.BusinessObjectiveId == id, cancellationToken),
            await db.BusinessObjectives.CountAsync(o => o.ParentObjectiveId == id, cancellationToken));

        return new BusinessObjectiveDetailDto(ToDto(objective), usage, kpis);
    }

    public async Task<BusinessObjectiveDto> CreateObjectiveAsync(
        BusinessObjectiveSaveDto dto,
        CancellationToken cancellationToken = default)
    {
        var input = Validate(dto);
        await EnsureCodeUniqueAsync(input.Code, null, cancellationToken);
        await EnsureParentValidAsync(null, dto.ParentObjectiveId, cancellationToken);

        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            ParentObjectiveId = dto.ParentObjectiveId,
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = time.GetUtcNow(),
        };
        Apply(objective, input);

        db.BusinessObjectives.Add(objective);
        await SaveAsync(input.Code, cancellationToken);
        return ToDto(objective);
    }

    public async Task<BusinessObjectiveDto> UpdateObjectiveAsync(
        Guid id,
        BusinessObjectiveSaveDto dto,
        CancellationToken cancellationToken = default)
    {
        var input = Validate(dto);
        var objective = await db.BusinessObjectives.SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Business Objective", id);

        if (objective.Status is not (BusinessObjectiveStatus.Draft or BusinessObjectiveStatus.Active))
        {
            throw new ConflictException(
                $"Mục tiêu kinh doanh đang ở trạng thái {objective.Status.ToEnumString()} nên không thể chỉnh sửa.");
        }

        await EnsureCodeUniqueAsync(input.Code, id, cancellationToken);
        await EnsureParentValidAsync(id, dto.ParentObjectiveId, cancellationToken);

        objective.ParentObjectiveId = dto.ParentObjectiveId;
        Apply(objective, input);
        objective.UpdatedAt = time.GetUtcNow();

        await SaveAsync(input.Code, cancellationToken);
        return ToDto(objective);
    }

    public async Task DeleteObjectiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var objective = await db.BusinessObjectives.SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Business Objective", id);

        var inUse = await db.ObjectiveKpis.AnyAsync(m => m.BusinessObjectiveId == id, cancellationToken)
            || await db.TargetAssignments.AnyAsync(t => t.BusinessObjectiveId == id, cancellationToken)
            || await db.BusinessCampaigns.AnyAsync(c => c.BusinessObjectiveId == id, cancellationToken)
            || await db.BusinessObjectives.AnyAsync(o => o.ParentObjectiveId == id, cancellationToken);
        if (inUse)
        {
            throw InUse();
        }

        db.BusinessObjectives.Remove(objective);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsForeignKeyViolation(ex))
        {
            // A record was linked between the check above and the delete.
            throw InUse();
        }
    }

    private static ConflictException InUse() =>
        new(ErrorCodes.GetDefaultMessage(ErrorCodes.BusinessObjectiveInUse), ErrorCodes.BusinessObjectiveInUse);

    private static BusinessObjectiveDto ToDto(BusinessObjective o) =>
        new(
            o.Id,
            o.ParentObjectiveId,
            o.Code,
            o.Name,
            o.Description,
            o.StartDate,
            o.EndDate,
            o.PriorityWeight,
            o.Status.ToEnumString(),
            o.UpdatedAt);

    private static void Apply(BusinessObjective objective, ObjectiveInput input)
    {
        objective.Code = input.Code;
        objective.Name = input.Name;
        objective.Description = input.Description;
        objective.StartDate = input.StartDate;
        objective.EndDate = input.EndDate;
        objective.PriorityWeight = input.PriorityWeight;
    }

    private ObjectiveInput Validate(BusinessObjectiveSaveDto dto)
    {
        var maxCode = db.MaxLength<BusinessObjective>(nameof(BusinessObjective.Code));
        var maxName = db.MaxLength<BusinessObjective>(nameof(BusinessObjective.Name));

        var code = dto.Code?.Trim() ?? string.Empty;
        var name = dto.Name?.Trim() ?? string.Empty;
        if (code.Length == 0 || code.Length > maxCode || name.Length == 0 || name.Length > maxName)
        {
            throw new ValidationException(
                $"Mã và tên mục tiêu là bắt buộc; mã tối đa {maxCode} và tên tối đa {maxName} ký tự.");
        }

        if (dto.StartDate is not { } startDate || dto.EndDate is not { } endDate)
        {
            throw new ValidationException("Ngày bắt đầu và ngày kết thúc là bắt buộc.");
        }

        if (endDate < startDate)
        {
            throw new ValidationException("Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu.");
        }

        if (dto.PriorityWeight is { } weight)
        {
            var bound = db.DecimalUpperBound<BusinessObjective>(nameof(BusinessObjective.PriorityWeight));
            if (weight < 0 || weight >= bound)
            {
                throw new ValidationException($"Trọng số ưu tiên phải lớn hơn hoặc bằng 0 và nhỏ hơn {bound}.");
            }
        }

        return new ObjectiveInput(
            code,
            name,
            string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            startDate,
            endDate,
            dto.PriorityWeight);
    }

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (await db.BusinessObjectives.AnyAsync(
                o => o.Code == code && (!excludeId.HasValue || o.Id != excludeId.Value),
                cancellationToken))
        {
            throw new ConflictException($"Mã mục tiêu '{code}' đã tồn tại.");
        }
    }

    /// <summary>The parent must exist and must not be the objective itself or one of its descendants.</summary>
    private async Task EnsureParentValidAsync(Guid? objectiveId, Guid? parentId, CancellationToken cancellationToken)
    {
        if (!parentId.HasValue)
        {
            return;
        }

        var visited = new HashSet<Guid>();
        Guid? current = parentId;
        while (current.HasValue && visited.Add(current.Value))
        {
            if (current == objectiveId)
            {
                throw new ValidationException("Mục tiêu cha không được là chính nó hoặc một mục tiêu con của nó.");
            }

            var step = current.Value;
            var row = await db.BusinessObjectives.AsNoTracking()
                .Where(o => o.Id == step)
                .Select(o => new { o.ParentObjectiveId })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Business Objective", step);
            current = row.ParentObjectiveId;
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
            throw new ConflictException($"Mã mục tiêu '{code}' đã tồn tại.");
        }
    }

    /// <summary>
    /// Null when every objective is readable (Branch scope or wider: an objective belongs to the branch, not to one
    /// department). For Dept scope, the departments of the caller's subtree: only objectives with a target there are readable.
    /// </summary>
    private async Task<HashSet<Guid>?> ResolveVisibleDepartmentsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(accountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "objective", "read");
        if (level < AccessScope.DeptLevel)
        {
            throw new ForbiddenException();
        }

        if (level >= AccessScope.BranchLevel)
        {
            return null;
        }

        var caller = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException();

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        return AccessScope.DepartmentsInScope(level, caller.DepartmentId, hierarchy) ?? [];
    }
}
