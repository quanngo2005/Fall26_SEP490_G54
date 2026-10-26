using System.Security.Claims;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;

namespace G54.Api.Authorization;

public sealed class PermissionAuthorizationHandler(IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (context.User.IsInRole("ADMIN")
            || context.User.IsInRole("Admin")
            || context.User.HasClaim("role", "ADMIN")
            || context.User.HasClaim("role", "Admin"))
        {
            context.Succeed(requirement);
            return;
        }

        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(sub, out var accountId))
        {
            return;
        }

        var hasPermission = await permissionService.HasPermissionAsync(
            accountId,
            requirement.Resource,
            requirement.Action,
            requirement.Scope,
            CancellationToken.None);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
