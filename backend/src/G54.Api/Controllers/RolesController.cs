using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.UserAccounts;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>Role list for the account role selector and SCR-ADM-03. System admin only.</summary>
[ApiController]
[Route("roles")]
[Authorize]
public sealed class RolesController(IUserAccountService service) : ControllerBase
{
    [HasPermission("role", "read", "All")]
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RoleSummaryDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleSummaryDto>>>> List(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.ListRolesAsync(User.GetRequiredUserId(), ct), traceId: HttpContext.TraceIdentifier));
}
