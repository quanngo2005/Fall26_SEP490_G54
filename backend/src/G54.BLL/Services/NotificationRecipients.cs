using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>Resolves active employee recipients by role or department management scope for automatic notifications.</summary>
internal static class NotificationRecipients
{
    private static readonly string[] AdminRoleCodes = ["ADMIN", "SYSTEM_ADMIN"];
    private static readonly string[] BranchManagerRoleCodes = ["BRANCH_MANAGER"];
    private static readonly string[] DeptManagerRoleCodes = ["MANAGER", "DEPT_MANAGER", "DEPARTMENT_MANAGER"];

    public static Task<List<Guid>> ActiveAdminsAsync(AppDbContext db, CancellationToken ct = default)
        => EmployeesInRolesAsync(db, AdminRoleCodes, null, ct);

    public static Task<List<Guid>> ActiveBranchManagersAsync(AppDbContext db, CancellationToken ct = default)
        => EmployeesInRolesAsync(db, BranchManagerRoleCodes, null, ct);

    public static Task<List<Guid>> ActiveDepartmentManagersAsync(AppDbContext db, Guid departmentId, CancellationToken ct = default)
        => EmployeesInRolesAsync(db, DeptManagerRoleCodes, departmentId, ct);

    private static async Task<List<Guid>> EmployeesInRolesAsync(
        AppDbContext db,
        IReadOnlyCollection<string> roleCodes,
        Guid? departmentId,
        CancellationToken ct)
    {
        var query = db.UserRoles.AsNoTracking()
            .Where(ur => roleCodes.Contains(ur.Role.Code)
                && ur.Role.Status == RecordStatus.Active
                && !ur.Account.IsLocked
                && ur.Account.Employee.Status == EmployeeStatus.Active);

        if (departmentId.HasValue)
        {
            query = query.Where(ur => ur.Account.Employee.DepartmentId == departmentId.Value);
        }

        return await query
            .Select(ur => ur.Account.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
    }
}
