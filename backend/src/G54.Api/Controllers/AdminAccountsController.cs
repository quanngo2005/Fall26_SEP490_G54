using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Admin;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Authorize]
[Route("")]
[HasPermission("role", "update", "System")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class AdminAccountsController(IAdminAccountService service) : ControllerBase
{
    [HttpGet("roles")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RoleSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleSummaryDto>>>> GetRoles(
        CancellationToken cancellationToken)
    {
        var roles = await service.GetRolesAsync(cancellationToken);
        return Ok(ApiResponse.Ok(roles, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("user-accounts")]
    [ProducesResponseType<ApiResponse<PagedResult<UserAccountSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserAccountSummaryDto>>>> GetAccounts(
        [FromQuery] UserAccountFilterDto filter,
        CancellationToken cancellationToken)
    {
        var accounts = await service.GetAccountsAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(accounts, traceId: HttpContext.TraceIdentifier));
    }
}
