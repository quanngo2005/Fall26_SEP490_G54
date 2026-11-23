using G54.BLL.Common.Constants;

namespace G54.BLL.Services.Import.EmployeeOrganization;

/// <summary>Maps a template "Vị trí" code to the <c>job_position.code</c> and role code commit must assign.</summary>
public static class PositionMapping
{
    private static readonly Dictionary<string, (string JobPositionCode, string RoleCode)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TELLER"] = (JobPositionCodes.Teller, "EMPLOYEE"),
        ["SALES_OFFICER"] = (JobPositionCodes.Sales, "EMPLOYEE"),
        ["DEPT_MANAGER"] = (JobPositionCodes.DeptManager, "DEPT_MANAGER"),
        ["BRANCH_MANAGER"] = (JobPositionCodes.BranchManager, "BRANCH_MANAGER"),
        ["QA"] = (JobPositionCodes.Qa, "VERIFIER"),
        ["HR_OFFICER"] = (JobPositionCodes.HrOfficer, "HR_OFFICER"),
        ["SYS_ADMIN"] = (JobPositionCodes.SysAdmin, "SYSTEM_ADMIN"),
    };

    /// <summary>Template codes in dropdown order.</summary>
    public static IReadOnlyList<string> Codes { get; } = [.. Map.Keys];

    public static bool IsKnown(string code) => Map.ContainsKey(code);

    public static (string JobPositionCode, string RoleCode) Resolve(string code) => Map[code];

    /// <summary>The role an account gets for a <c>job_position.code</c>, or null when the position has no mapping.</summary>
    public static string? RoleCodeFor(string jobPositionCode)
    {
        foreach (var (position, role) in Map.Values)
        {
            if (string.Equals(position, jobPositionCode, StringComparison.OrdinalIgnoreCase))
            {
                return role;
            }
        }

        return null;
    }
}
