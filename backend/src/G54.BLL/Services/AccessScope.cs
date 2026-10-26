using G54.BLL.Common.Constants;
using G54.BLL.Dtos.Permission;

namespace G54.BLL.Services;

/// <summary>Resolves the scope a caller holds for one permission, and the departments that scope covers.</summary>
internal static class AccessScope
{
    public static readonly int OwnLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Own);
    public static readonly int DeptLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Dept);
    public static readonly int BranchLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Branch);
    public static readonly int AllLevel = PermissionScopes.GetScopeLevel(PermissionScopes.All);

    /// <summary>Highest scope level granted for <paramref name="resource"/>:<paramref name="action"/> (or the FULL_ACCESS wildcard). 0 when none.</summary>
    public static int LevelFor(IEnumerable<UserPermissionDto> granted, string resource, string action) =>
        granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, resource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, action, StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// Departments covered by a Dept (own department subtree) or Branch (subtree of the nearest branch) scope.
    /// Returns null for All (unrestricted) and an empty set below Dept (Own or none: the caller is limited to self).
    /// </summary>
    public static HashSet<Guid>? DepartmentsInScope(int level, Guid departmentId, DepartmentHierarchy hierarchy)
    {
        if (level >= AllLevel)
        {
            return null;
        }

        if (level < DeptLevel || !hierarchy.Nodes.ContainsKey(departmentId))
        {
            return [];
        }

        var root = level >= BranchLevel ? hierarchy.BranchRoot(departmentId) : departmentId;
        return hierarchy.Subtree(root).ToHashSet();
    }
}
