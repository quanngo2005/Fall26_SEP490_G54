using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Handover;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Handover API. Contract: docs/api/HANDOVER_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Authorize]
[Route("works/{workId:guid}/handovers")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class HandoversController(IHandoverService handoverService) : ControllerBase
{
    [HttpPost]
    [HasPermission("handover", "create", "Own")]
    [ProducesResponseType<ApiResponse<HandoverDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<HandoverDetailDto>>> CreateHandover(
        Guid workId,
        [FromBody] CreateHandoverRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await handoverService.CreateHandoverAsync(workId, User.GetRequiredUserId(), request, cancellationToken);
        var response = ApiResponse.Ok(
            result,
            SuccessMessages.GetMessage(SuccessMessages.HandoverRequested),
            HttpContext.TraceIdentifier);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
