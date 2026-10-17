using Microsoft.AspNetCore.Authorization;

namespace G54.Api.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission:";

    public string Resource { get; }
    public string Action { get; }
    public string Scope { get; }

    public HasPermissionAttribute(string resource, string action, string scope = "Own")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        Resource = resource.Trim();
        Action = action.Trim();
        Scope = scope.Trim();

        Policy = $"{PolicyPrefix}{Resource}:{Action}:{Scope}";
    }
}
