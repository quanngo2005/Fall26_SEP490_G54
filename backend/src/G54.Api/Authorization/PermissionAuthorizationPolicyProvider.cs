using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace G54.Api.Authorization;

public sealed class PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = policyName[HasPermissionAttribute.PolicyPrefix.Length..].Split(':');
            if (parts.Length >= 2)
            {
                var resource = parts[0];
                var action = parts[1];
                var scope = parts.Length >= 3 ? parts[2] : "Own";

                return new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(resource, action, scope))
                    .Build();
            }
        }

        return await base.GetPolicyAsync(policyName);
    }
}
