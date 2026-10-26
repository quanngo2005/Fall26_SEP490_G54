using System.Text.RegularExpressions;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Department;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

// Tree view and create / update / status / delete. Callers are authorised at the API layer (department:manage, All).
public sealed partial class DepartmentService
{
    private static readonly Regex CodePattern = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public async Task<IReadOnlyList<DepartmentTreeNodeDto>> GetTreeAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var org = await LoadOrgAsync(cancellationToken);
        var visible = await ResolveVisibleAsync(accountId, org, cancellationToken);
        var counts = await CountEmployeesByDepartmentAsync(cancellationToken);

        bool IsVisible(Guid id) => visible is null || visible.Contains(id);

        var roots = org.Nodes.Values
            .Where(n => n.Status != RecordStatus.Archived && IsVisible(n.Id) && (!n.ParentId.HasValue || !IsVisible(n.ParentId.Value) || !org.Nodes.ContainsKey(n.ParentId.Value)))
            .OrderBy(n => n.Code, StringComparer.Ordinal);

        var placed = new HashSet<Guid>();
        return roots.Select(n => BuildTreeNode(n, org, counts, placed)).ToList();
    }

    public async Task<DepartmentDetailDto> CreateDepartmentAsync(
        CreateDepartmentDto dto,
        CancellationToken cancellationToken = default)
    {
        var input = ValidateInput(dto.Code, dto.Name, dto.Description, dto.LevelType);
        var org = await LoadOrgAsync(cancellationToken);

        await EnsureCodeUniqueAsync(input.Code, null, cancellationToken);
        EnsurePlacement(org, null, input.Level, dto.ParentDepartmentId, parentChanged: true);

        var now = time.GetUtcNow();
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = input.Code,
            Name = input.Name,
            Description = input.Description,
            LevelType = input.Level,
            ParentDepartmentId = dto.ParentDepartmentId,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        db.Departments.Add(department);
        await db.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(await LoadOrgAsync(cancellationToken), department.Id, cancellationToken);
    }

    public async Task<DepartmentDetailDto> UpdateDepartmentAsync(
        Guid departmentId,
        UpdateDepartmentDto dto,
        CancellationToken cancellationToken = default)
    {
        var input = ValidateInput(dto.Code, dto.Name, dto.Description, dto.LevelType);
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new NotFoundException("Phòng ban", departmentId);
        var org = await LoadOrgAsync(cancellationToken);

        await EnsureCodeUniqueAsync(input.Code, departmentId, cancellationToken);
        EnsurePlacement(
            org,
            departmentId,
            input.Level,
            dto.ParentDepartmentId,
            parentChanged: dto.ParentDepartmentId != department.ParentDepartmentId);

        department.Code = input.Code;
        department.Name = input.Name;
        department.Description = input.Description;
        department.LevelType = input.Level;
        department.ParentDepartmentId = dto.ParentDepartmentId;
        department.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(await LoadOrgAsync(cancellationToken), departmentId, cancellationToken);
    }

    public async Task<DepartmentDetailDto> ChangeStatusAsync(
        Guid departmentId,
        DepartmentStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var status = EnumApiFormat.ParseRequiredEnum<RecordStatus>(dto.Status, "status");
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new NotFoundException("Phòng ban", departmentId);

        if (department.Status != status)
        {
            if (status == RecordStatus.Active)
            {
                await EnsureParentActiveAsync(department.ParentDepartmentId, cancellationToken);
            }
            else if (department.Status == RecordStatus.Active)
            {
                await EnsureCanStopAsync(departmentId, "ngừng hoạt động", includeInactiveChildren: false, cancellationToken);
            }

            department.Status = status;
            department.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        return await BuildDetailAsync(await LoadOrgAsync(cancellationToken), departmentId, cancellationToken);
    }

    /// <summary>Soft delete: the row is kept (history, targets, works) and marked ARCHIVED, which hides it from lists and the tree.</summary>
    public async Task DeleteDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default)
    {
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new NotFoundException("Phòng ban", departmentId);

        if (department.Status == RecordStatus.Archived)
        {
            return;
        }

        await EnsureCanStopAsync(departmentId, "xóa", includeInactiveChildren: true, cancellationToken);

        department.Status = RecordStatus.Archived;
        department.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    // ----- Validation -----------------------------------------------------------------------

    private static (string Code, string Name, string? Description, DepartmentLevelType Level) ValidateInput(
        string code,
        string name,
        string? description,
        string levelType)
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (trimmedCode.Length is 0 or > 50 || !CodePattern.IsMatch(trimmedCode))
        {
            throw new ValidationException("Mã phòng ban là bắt buộc, tối đa 50 ký tự và chỉ gồm chữ cái, chữ số, dấu gạch ngang hoặc gạch dưới.");
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is 0 or > 200)
        {
            throw new ValidationException("Tên phòng ban là bắt buộc và không quá 200 ký tự.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > 1000 })
        {
            throw new ValidationException("Mô tả không vượt quá 1000 ký tự.");
        }

        var level = EnumApiFormat.ParseRequiredEnum<DepartmentLevelType>(levelType, "levelType");
        return (trimmedCode.ToUpperInvariant(), trimmedName, trimmedDescription, level);
    }

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await db.Departments.AnyAsync(
            d => d.Code == code && (!excludeId.HasValue || d.Id != excludeId.Value),
            cancellationToken);

        if (exists)
        {
            throw new ConflictException($"Mã phòng ban '{code}' đã tồn tại trong hệ thống.");
        }
    }

    /// <summary>
    /// A BRANCH is a root; a DEPARTMENT sits under a BRANCH and a TEAM under a DEPARTMENT (the parent must always be
    /// a higher level). The new parent may not be the department itself or one of its descendants, and the new level
    /// must stay above the levels of the existing children.
    /// </summary>
    private static void EnsurePlacement(
        DepartmentHierarchy org,
        Guid? departmentId,
        DepartmentLevelType level,
        Guid? parentId,
        bool parentChanged)
    {
        if (level == DepartmentLevelType.Branch)
        {
            if (parentId.HasValue)
            {
                throw new ValidationException("Chi nhánh là cấp cao nhất nên không có phòng ban cha.");
            }
        }
        else
        {
            if (!parentId.HasValue)
            {
                throw new ValidationException("Phòng ban và nhóm phải thuộc một đơn vị cấp trên (parentDepartmentId).");
            }

            if (!org.Nodes.TryGetValue(parentId.Value, out var parent))
            {
                throw new NotFoundException("Phòng ban cha", parentId.Value);
            }

            if (departmentId.HasValue && org.Subtree(departmentId.Value).Contains(parent.Id))
            {
                throw new ValidationException("Không thể chọn chính phòng ban này hoặc đơn vị trực thuộc của nó làm phòng ban cha.");
            }

            if ((int)parent.Level >= (int)level)
            {
                throw new ValidationException("Phòng ban cha phải có cấp cao hơn: Chi nhánh > Phòng ban > Nhóm.");
            }

            if (parentChanged && parent.Status != RecordStatus.Active)
            {
                throw new ValidationException($"Phòng ban cha '{parent.Name}' hiện không ở trạng thái hoạt động.");
            }
        }

        if (departmentId.HasValue
            && org.ChildrenOf(departmentId.Value).Any(childId => (int)org.Nodes[childId].Level <= (int)level))
        {
            throw new ValidationException("Cấp mới không phù hợp: các đơn vị trực thuộc phải có cấp thấp hơn.");
        }
    }

    private async Task EnsureParentActiveAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (!parentId.HasValue)
        {
            return;
        }

        var parent = await db.Departments.AsNoTracking()
            .Where(d => d.Id == parentId.Value)
            .Select(d => new { d.Name, d.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (parent is not null && parent.Status != RecordStatus.Active)
        {
            throw new ValidationException($"Phòng ban cha '{parent.Name}' đang không hoạt động nên chưa thể kích hoạt lại phòng ban này.");
        }
    }

    private async Task EnsureCanStopAsync(
        Guid departmentId,
        string action,
        bool includeInactiveChildren,
        CancellationToken cancellationToken)
    {
        var children = await db.Departments.CountAsync(
            d => d.ParentDepartmentId == departmentId
                && (includeInactiveChildren ? d.Status != RecordStatus.Archived : d.Status == RecordStatus.Active),
            cancellationToken);
        if (children > 0)
        {
            throw new ConflictException($"Không thể {action}: còn {children} đơn vị trực thuộc chưa được {(includeInactiveChildren ? "xóa" : "ngừng hoạt động")}. Hãy xử lý các đơn vị đó trước.");
        }

        var employees = await db.Employees.CountAsync(
            e => e.DepartmentId == departmentId && e.Status != EmployeeStatus.Terminated,
            cancellationToken);
        if (employees > 0)
        {
            throw new ConflictException($"Không thể {action}: còn {employees} nhân viên thuộc phòng ban này. Hãy chuyển họ sang phòng ban khác trước.");
        }
    }

    // ----- Tree -----------------------------------------------------------------------------

    private static DepartmentTreeNodeDto BuildTreeNode(
        DepartmentNode node,
        DepartmentHierarchy org,
        Dictionary<Guid, int> counts,
        HashSet<Guid> placed)
    {
        placed.Add(node.Id);
        var children = org.ChildrenOf(node.Id)
            .Where(id => !placed.Contains(id))
            .Select(id => org.Nodes[id])
            .Where(n => n.Status != RecordStatus.Archived)
            .OrderBy(n => n.Code, StringComparer.Ordinal)
            .Select(n => BuildTreeNode(n, org, counts, placed))
            .ToList();

        return new DepartmentTreeNodeDto(
            node.Id,
            node.Code,
            node.Name,
            node.Level.ToEnumString(),
            node.Status.ToEnumString(),
            counts.GetValueOrDefault(node.Id),
            org.Subtree(node.Id).Sum(id => counts.GetValueOrDefault(id)),
            children);
    }
}
