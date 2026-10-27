using G54.BLL.Common.Constants;
using Microsoft.AspNetCore.Authorization;

namespace G54.Api.Authorization;

/// <summary>
/// Role gate for the organization screens (departments and job positions). Only a system admin, a branch manager
/// or a department manager may call these endpoints; every other role (employee, HR, QA, verifier, ...) gets 403.
/// Apply with <c>[Authorize(Policy = OrganizationAccess.Policy)]</c>. Data scope inside the endpoint is unchanged.
/// </summary>
public static class OrganizationAccess
{
    public const string Policy = "OrganizationViewer";

    public static void AddOrganizationPolicy(this AuthorizationOptions options) =>
        options.AddPolicy(Policy, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => RoleCodes.CanViewOrganization(
                context.User.FindAll("role").Select(claim => claim.Value))));
}
