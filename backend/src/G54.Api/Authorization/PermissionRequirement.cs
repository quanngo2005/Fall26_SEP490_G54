using Microsoft.AspNetCore.Authorization;

namespace G54.Api.Authorization;

public sealed class PermissionRequirement(string resource, string action, string scope) : IAuthorizationRequirement
{
    public string Resource { get; } = resource;
    public string Action { get; } = action;
    public string Scope { get; } = scope;
}
