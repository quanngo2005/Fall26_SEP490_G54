using G54.BLL.Common.Exceptions;
using G54.BLL.Services;
using G54.DAL.Uow;

namespace G54.BLL.Common.Validation;

/// <summary>
/// Branch/department access check shared by every target-assignment service (allocation and year-target
/// import): ADMIN (<c>target:manage:All</c>) sees everything; a MANAGER only sees their own department subtree.
/// </summary>
internal static class DepartmentAccessGuard
{
    public const string AccessDenied = "Bạn không có quyền với target của đơn vị này.";

    /// <summary>Returns null for admins (target:manage:All); otherwise the caller's department subtree.</summary>
    public static async Task<HashSet<Guid>?> ResolveVisibleAsync(IUnitOfWork uow, IPermissionService permissions, Guid userId, CancellationToken ct)
    {
        if (await permissions.HasPermissionAsync(userId, "target", "manage", "All", ct))
        {
            return null;
        }

        var callerDepartmentId = await uow.TargetAssignments.GetAccountDepartmentIdAsync(userId, ct)
            ?? throw new ForbiddenException(AccessDenied);
        return [.. await uow.TargetAssignments.GetDescendantDepartmentIdsAsync(callerDepartmentId, ct)];
    }

    public static async Task EnsureDepartmentAccessAsync(IUnitOfWork uow, IPermissionService permissions, Guid userId, Guid? departmentId, CancellationToken ct)
    {
        var visible = await ResolveVisibleAsync(uow, permissions, userId, ct);
        if (visible is not null && (departmentId is null || !visible.Contains(departmentId.Value)))
        {
            throw new ForbiddenException(AccessDenied);
        }
    }
}
