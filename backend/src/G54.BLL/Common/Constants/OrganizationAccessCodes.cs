namespace G54.BLL.Common.Constants;

/// <summary>
/// The permission that gates the organization screens (SCR-ORG-01 departments, SCR-ORG-02 job positions).
/// It is granted through <c>role_permission</c> (see migration <c>SeedOrganizationReadAccess</c>): <c>All</c> to the
/// system admin, <c>Branch</c> to the branch manager, <c>Dept</c> to the department manager. A role without a row is rejected.
/// </summary>
public static class OrganizationAccessCodes
{
    public const string Resource = "organization";
    public const string Read = "read";
    public const string Code = "ORGANIZATION_READ";
}
