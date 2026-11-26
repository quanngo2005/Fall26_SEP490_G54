namespace G54.BLL.Common.Constants;

/// <summary>
/// The six roles of the system, one per actor. There are no aliases: the database, the JWT role claims and the sidebar all use
/// these exact codes. No role skips permission checks: every role, <see cref="SystemAdmin"/> included, holds explicit grants.
/// </summary>
public static class RoleCodes
{
    public const string Employee = "EMPLOYEE";
    public const string DeptManager = "DEPT_MANAGER";
    public const string BranchManager = "BRANCH_MANAGER";
    public const string HrOfficer = "HR_OFFICER";
    public const string Verifier = "VERIFIER";
    public const string SystemAdmin = "SYSTEM_ADMIN";

    public static readonly IReadOnlyList<string> All =
        [Employee, DeptManager, BranchManager, HrOfficer, Verifier, SystemAdmin];

    /// <summary>The sidebar workspace a role opens, or null for a code that is not one of the six roles.</summary>
    public static string? Workspace(string code) => code.Trim().ToUpperInvariant() switch
    {
        Employee => "employee",
        DeptManager => "departmentManager",
        BranchManager => "branchManager",
        HrOfficer => "hr",
        Verifier => "qa",
        SystemAdmin => "admin",
        _ => null,
    };
}
