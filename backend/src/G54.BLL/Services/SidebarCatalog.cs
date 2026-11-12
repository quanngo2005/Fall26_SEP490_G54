namespace G54.BLL.Services;

public static class SidebarCatalog
{
    public static IReadOnlySet<string> Routes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "", "/home", "/my-work", "/my-targets", "/my-evaluation", "/departments", "/department-performance", "/employee-profile", "/work-types", "/work-type-detail", "/review-verify", "/appeals", "/kpi-definitions", "/sidebar-settings", "/audit-logs",
    };
    public static IReadOnlyDictionary<string, string[]> Defaults { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["employee"] = ["overview", "myWork", "mySubmissions", "myPerformance", "myTargets", "appeals", "notifications", "myProfile"],
        ["departmentManager"] = ["overview", "employees", "teamWork", "handoverDelegation", "reviewVerify", "teamPerformance", "targetOverview", "targetsAndCampaigns", "appeals", "notifications", "myProfile"],
        ["branchManager"] = ["overview", "organization", "departments", "jobPositions", "reportingLines", "myWork", "branchWork", "handoverDelegation", "reviewVerify", "teamPerformance", "branchPerformance", "departmentPerformance", "targetOverview", "targetsAndCampaigns", "kpiDefinitions", "appeals", "notifications", "myProfile"],
        ["hr"] = ["overview", "employees", "organization", "departments", "jobPositions", "reportingLines", "employeeData", "teamPerformance", "appeals", "notifications", "myProfile"],
        ["qa"] = ["overview", "verificationQueue", "appeals", "escalatedCases", "auditReview", "riskEvents", "reportsAnalytics", "notifications", "myProfile"],
        ["admin"] = ["overview", "accounts", "rolesPermissions", "accessPolicies", "workTypes", "kpiDefinitions", "systemConfiguration", "systemAudit", "myProfile"],
    };

    public static IReadOnlySet<string> ItemIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "overview", "myWork", "mySubmissions", "myTargets", "myPerformance", "notifications", "myProfile", "employees", "organization",
        "departments", "jobPositions", "reportingLines", "employeeData", "teamWork", "handoverDelegation",
        "reviewVerify", "teamPerformance", "targetsAndCampaigns", "targetOverview", "branchWork", "branchPerformance", "departmentPerformance",
        "appeals", "reportingStructure", "verificationQueue", "escalatedCases", "auditReview", "riskEvents", "reportsAnalytics", "accounts",
        "rolesPermissions", "accessPolicies", "systemConfiguration", "systemAudit", "tasks", "personalKpis", "review", "positions",
        "kpiDefinitions", "workTypes", "targets", "reports", "audit", "users", "settings",
    };

    public static IReadOnlySet<string> Children { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "departments", "jobPositions", "reportingLines",
    };

    public static string? MapRole(string code) => code.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_') switch
    {
        "EMPLOYEE" => "employee",
        "MANAGER" or "DEPT_MANAGER" or "DEPARTMENT_MANAGER" => "departmentManager",
        "BRANCH_MANAGER" => "branchManager",
        "HR" or "HR_MANAGER" => "hr",
        "QA" or "COMPLIANCE" or "VERIFIER" => "qa",
        "ADMIN" or "SYSTEM_ADMIN" => "admin",
        _ => null,
    };
}
