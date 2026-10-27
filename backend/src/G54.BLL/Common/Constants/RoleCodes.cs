namespace G54.BLL.Common.Constants;

/// <summary>
/// Role codes carried in the JWT <c>role</c> claim, and the groups the organization screens (SCR-ORG-01 / SCR-ORG-02)
/// are restricted to. Codes are compared case-insensitively after trimming and normalising spaces/hyphens to
/// underscores, the same normalisation <see cref="G54.BLL.Services.SidebarCatalog.MapRole"/> applies.
/// </summary>
public static class RoleCodes
{
    public const string SystemAdmin = "SYSTEM_ADMIN";
    public const string Admin = "ADMIN";
    public const string BranchManager = "BRANCH_MANAGER";
    public const string DeptManager = "DEPT_MANAGER";
    public const string DepartmentManager = "DEPARTMENT_MANAGER";
    /// <summary>Seeded manager role; behaves as a department manager.</summary>
    public const string Manager = "MANAGER";

    /// <summary>Roles allowed to open the organization screens: system admin, branch manager and department manager.</summary>
    public static readonly IReadOnlySet<string> OrganizationViewers = new HashSet<string>(StringComparer.Ordinal)
    {
        SystemAdmin,
        Admin,
        BranchManager,
        DeptManager,
        DepartmentManager,
        Manager,
    };

    public static string Normalize(string? role) =>
        (role ?? string.Empty).Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');

    public static bool CanViewOrganization(IEnumerable<string> roles) =>
        roles.Any(role => OrganizationViewers.Contains(Normalize(role)));
}
