namespace G54.BLL.Common.Constants;

public static class PermissionScopes
{
    public const string Own = "Own";
    public const string Dept = "Dept";
    public const string Branch = "Branch";
    public const string All = "All";
    public const string System = "System";

    public static int GetScopeLevel(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return 0;
        }

        return scope.Trim().ToLowerInvariant() switch
        {
            "all" or "system" or "*" => 4,
            "branch" => 3,
            "dept" or "department" or "managed" or "team" => 2,
            "own" or "self" => 1,
            _ => 0,
        };
    }

    public static bool IsSatisfied(string? grantedScope, string? requiredScope)
    {
        var grantedLevel = GetScopeLevel(grantedScope);
        var requiredLevel = GetScopeLevel(requiredScope);
        return grantedLevel >= requiredLevel;
    }
}
