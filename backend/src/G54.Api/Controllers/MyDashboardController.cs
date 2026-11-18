using System.Threading;
using System.Threading.Tasks;
using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Dashboard;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Operational Personal / Home Dashboard endpoints (SCR-HOME-01).
/// Serves KPI progress cards for Teller and Sales Officer roles.
/// Contract: docs/api/MY_DASHBOARD_PROGRESS_CARD_CONTRACT.md.
/// </summary>
[ApiController]
[Route("my-dashboard")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class MyDashboardController(IMyDashboardService service) : ControllerBase
{
    /// <summary>
    /// Gets the operational monthly or quarterly KPI progress card for the calling employee.
    /// </summary>
    [HttpGet("progress-card")]
    [ProducesResponseType<ApiResponse<MyProgressCardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MyProgressCardDto>>> GetProgressCard(
        [FromQuery] MyProgressCardQueryDto query,
        CancellationToken cancellationToken)
    {
        var result = await service.GetMyProgressCardAsync(User.GetRequiredUserId(), query, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}

