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
[Route("")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class HandoversController(IHandoverService handoverService) : ControllerBase
{
    /// <summary>Colleagues of the caller's department that a work can be handed over to.</summary>
    [HttpGet("handovers/recipients")]
    [HasPermission("handover", "create", "Own")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HandoverRecipientDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<HandoverRecipientDto>>>> GetRecipients(
        CancellationToken cancellationToken)
    {
        var result = await handoverService.GetRecipientsAsync(User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("works/{workId:guid}/handovers")]
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
        var response = ApiResponse.Ok(result, SuccessMessages.GetMessage(SuccessMessages.HandoverTransferred), HttpContext.TraceIdentifier);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
