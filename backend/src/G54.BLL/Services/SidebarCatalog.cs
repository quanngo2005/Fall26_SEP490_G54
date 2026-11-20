namespace G54.BLL.Services;

public static class SidebarCatalog
{
    public static IReadOnlySet<string> Routes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "",
        "/home",
        "/my-work",
        "/my-targets",
        "/my-evaluation",
        "/departments",
        "/department-performance",
        "/branch-performance",
        "/employee-detail",
        "/employee-profile",
        "/work-types",
        "/evaluation-profiles",
        "/work-type-detail",
        "/review-verify",
        "/appeals",
        "/notifications",
        "/reconciliations",
        "/data-import",
        "/import-history",
        "/kpi-definitions",
        "/campaigns",
        "/target-overview",
        "/sidebar-settings",
        "/system-parameters",
        "/ranking-studio",
        "/roles",
        "/user-accounts",
        "/audit-logs",
    };

    public static IReadOnlySet<string> AdminOnlyRoutes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "/sidebar-settings",
        "/system-parameters",
        "/ranking-studio",
        "/roles",
        "/user-accounts",
        "/audit-logs",
    };

    public static IReadOnlyDictionary<string, string[]> Defaults { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["employee"] = ["overview", "myWork", "mySubmissions", "myPerformance", "myTargets", "appeals", "notifications", "myProfile"],
        ["departmentManager"] = ["overview", "employees", "teamWork", "handoverDelegation", "reviewVerify", "reconciliation", "teamPerformance", "departmentPerformance", "targetOverview", "targetsAndCampaigns", "appeals", "notifications", "myProfile"],
        ["branchManager"] = ["overview", "organization", "departments", "jobPositions", "reportingLines", "myWork", "branchWork", "handoverDelegation", "reviewVerify", "reconciliation", "branchPerformance", "departmentPerformance", "targetOverview", "targetsAndCampaigns", "kpiDefinitions", "appeals", "notifications", "myProfile"],
        ["hr"] = ["overview", "employees", "organization", "departments", "jobPositions", "reportingLines", "employeeData", "appeals", "notifications", "myProfile"],
        ["qa"] = ["overview", "dataImport", "importHistory", "reconciliation", "verificationQueue", "appeals", "riskEvents", "notifications", "myProfile"],
        ["admin"] = ["overview", "accounts", "rolesPermissions", "accessPolicies", "workTypes", "kpiDefinitions", "systemConfiguration", "systemParameters", "rankingStudio", "systemAudit", "dataImport", "importHistory", "notifications", "myProfile"],
    };

    public static IReadOnlySet<string> ItemIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "overview", "myWork", "mySubmissions", "myTargets", "myPerformance", "notifications", "myProfile", "employees", "organization",
        "departments", "jobPositions", "reportingLines", "employeeData", "teamWork", "handoverDelegation",
        "reviewVerify", "reconciliation", "dataImport", "importHistory", "teamPerformance", "targetsAndCampaigns", "targetOverview", "branchWork", "branchPerformance", "departmentPerformance",
        "appeals", "reportingStructure", "verificationQueue", "escalatedCases", "auditReview", "riskEvents", "reportsAnalytics", "accounts",
        "rolesPermissions", "accessPolicies", "systemConfiguration", "systemParameters", "rankingStudio", "systemAudit", "tasks", "personalKpis", "review", "positions",
        "kpiDefinitions", "workTypes", "targets", "reports", "audit", "users", "settings",
    };

    public static IReadOnlySet<string> Children { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "departments", "jobPositions", "reportingLines",
    };

    public static string? MapRole(string code) => G54.BLL.Common.Constants.RoleCodes.Workspace(code);
}
