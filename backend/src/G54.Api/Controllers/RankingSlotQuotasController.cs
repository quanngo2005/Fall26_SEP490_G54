using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.SystemParameter;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>Ranking slot quotas per department (A1, A2, A3, B headcount). Tier C takes the remaining employees.</summary>
[ApiController]
[Route("ranking-slot-quotas")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class RankingSlotQuotasController(IRankingSlotQuotaService service) : ControllerBase
{
    [HttpGet]
    [HasPermission("system_parameter", "view", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RankingSlotQuotasDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RankingSlotQuotasDto>>> Get(
        [FromQuery] Guid departmentId,
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await service.GetAsync(departmentId, periodId, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpPut]
    [HasPermission("system_parameter", "update", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RankingSlotQuotasDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RankingSlotQuotasDto>>> Update(
        UpdateRankingSlotQuotasDto request,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await service.UpdateAsync(request, cancellationToken), traceId: HttpContext.TraceIdentifier));
}
