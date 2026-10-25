using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

// Filtered, paged work list (GET /works), limited to the caller's work:read scope.
public sealed partial class WorkService
{
    private static readonly int OwnScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Own);
    private static readonly int DepartmentScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Dept);
    private static readonly int BranchScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Branch);
    private static readonly int AllScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.All);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons",
        Justification = "EF Core must translate this predicate to SQL; Npgsql cannot translate StringComparison overloads.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL lower(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToLower() to SQL lower().")]
    public async Task<PagedResult<WorkListItemDto>> GetWorksAsync(
        Guid accountId,
        WorkFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var statuses = ParseStatuses(filter.Status);
        var priority = EnumApiFormat.ParseEnum<WorkPriority>(filter.Priority, "priority");

        var caller = await ResolveCallerAsync(accountId, cancellationToken);
        var hierarchy = await DepartmentHierarchy.LoadAsync(unitOfWork.Departments.Query(), cancellationToken);

        // null = no restriction. Otherwise the set of departments whose employees' works the caller may see.
        var visibleDepartments = ResolveVisibleDepartments(caller, hierarchy);
        var ownOnly = visibleDepartments is null && caller.Level < AllScopeLevel;

        var query = unitOfWork.Works.Query();

        if (ownOnly)
        {
            query = query.Where(w => w.AssigneeEmployeeId == caller.EmployeeId);
        }
        else if (visibleDepartments is not null)
        {
            query = query.Where(w => visibleDepartments.Contains(w.AssigneeEmployee.DepartmentId));
        }

        if (filter.DepartmentId.HasValue)
        {
            var departmentId = filter.DepartmentId.Value;
            if (!hierarchy.Nodes.ContainsKey(departmentId))
            {
                throw new NotFoundException("Phòng ban", departmentId);
            }

            if (visibleDepartments is not null && !visibleDepartments.Contains(departmentId))
            {
                throw new ForbiddenException("Bạn không có quyền xem công việc của phòng ban này.");
            }

            var departments = filter.IncludeChildDepartments ? hierarchy.Subtree(departmentId) : [departmentId];
            query = query.Where(w => departments.Contains(w.AssigneeEmployee.DepartmentId));
        }

        if (statuses.Count > 0)
        {
            query = query.Where(w => statuses.Contains(w.Status));
        }

        if (filter.AssigneeEmployeeId.HasValue)
        {
            query = query.Where(w => w.AssigneeEmployeeId == filter.AssigneeEmployeeId.Value);
        }

        if (filter.WorkTypeId.HasValue)
        {
            query = query.Where(w => w.WorkTypeId == filter.WorkTypeId.Value);
        }

        if (priority.HasValue)
        {
            query = query.Where(w => w.Priority == priority.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim().ToLowerInvariant();
            query = query.Where(w => w.Code.ToLower().Contains(keyword) || w.Title.ToLower().Contains(keyword));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(w => w.UpdatedAt)
            .ThenBy(w => w.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new
            {
                w.Id,
                w.Code,
                w.Title,
                w.WorkTypeId,
                WorkTypeName = w.WorkType != null ? w.WorkType.Name : null,
                w.BusinessCampaignId,
                w.AssigneeEmployeeId,
                AssigneeCode = w.AssigneeEmployee.EmployeeCode,
                AssigneeName = w.AssigneeEmployee.FullName,
                DepartmentId = w.AssigneeEmployee.DepartmentId,
                DepartmentCode = w.AssigneeEmployee.Department.Code,
                DepartmentName = w.AssigneeEmployee.Department.Name,
                w.Priority,
                w.Status,
                w.PlannedStartDate,
                w.PlannedEndDate,
                w.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new WorkListItemDto(
                r.Id,
                r.Code,
                r.Title,
                r.WorkTypeId,
                r.WorkTypeName,
                r.BusinessCampaignId,
                r.AssigneeEmployeeId,
                r.AssigneeCode,
                r.AssigneeName,
                r.DepartmentId,
                r.DepartmentCode,
                r.DepartmentName,
                r.Priority.ToString(),
                r.Status.ToString(),
                r.PlannedStartDate,
                r.PlannedEndDate,
                r.UpdatedAt))
            .ToList();

        return new PagedResult<WorkListItemDto>(items, totalCount, page, pageSize);
    }

    private static List<WorkStatus> ParseStatuses(List<string>? raw)
    {
        var result = new List<WorkStatus>();
        foreach (var value in raw ?? [])
        {
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var status = EnumApiFormat.ParseEnum<WorkStatus>(NormalizeStatusInput(part), "status");
                if (status.HasValue && !result.Contains(status.Value))
                {
                    result.Add(status.Value);
                }
            }
        }

        return result;
    }

    private async Task<Caller> ResolveCallerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await unitOfWork.Repository<Account>().Query()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.EmployeeId, a.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException();

        var granted = await permissions.GetEffectivePermissionsAsync(accountId, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, "work", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, "read", StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level < OwnScopeLevel)
        {
            throw new ForbiddenException();
        }

        return new Caller(account.EmployeeId, account.DepartmentId, level);
    }

    /// <summary>Null when the caller is unrestricted (All) or limited to their own works (Own).</summary>
    private static HashSet<Guid>? ResolveVisibleDepartments(Caller caller, DepartmentHierarchy hierarchy)
    {
        if (caller.Level >= AllScopeLevel || caller.Level < DepartmentScopeLevel)
        {
            return null;
        }

        if (!hierarchy.Nodes.ContainsKey(caller.DepartmentId))
        {
            throw new ForbiddenException();
        }

        var root = caller.Level >= BranchScopeLevel ? hierarchy.BranchRoot(caller.DepartmentId) : caller.DepartmentId;
        return hierarchy.Subtree(root).ToHashSet();
    }

    private sealed record Caller(Guid EmployeeId, Guid DepartmentId, int Level);
}
