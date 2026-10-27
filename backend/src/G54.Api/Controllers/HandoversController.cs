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
        var response = ApiResponse.Ok(
            result,
            SuccessMessages.GetMessage(result.Status == "ACCEPTED" ? SuccessMessages.HandoverTransferred : SuccessMessages.HandoverRequested),
            HttpContext.TraceIdentifier);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("handovers/{id:guid}/accept")]
    [HasPermission("handover", "respond", "Own")]
    [ProducesResponseType<ApiResponse<HandoverDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<HandoverDetailDto>>> AcceptHandover(Guid id, CancellationToken cancellationToken)
    {
        var result = await handoverService.AcceptHandoverAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(
            result,
            SuccessMessages.GetMessage(SuccessMessages.HandoverAccepted),
            HttpContext.TraceIdentifier));
    }

    [HttpPost("handovers/{id:guid}/reject")]
    [HasPermission("handover", "respond", "Own")]
    [ProducesResponseType<ApiResponse<HandoverDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<HandoverDetailDto>>> RejectHandover(Guid id, CancellationToken cancellationToken)
    {
        var result = await handoverService.RejectHandoverAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(
            result,
            SuccessMessages.GetMessage(SuccessMessages.HandoverRejected),
            HttpContext.TraceIdentifier));
    }

    [HttpPost("handovers/{id:guid}/manager/approve")]
    [HasPermission("handover", "manage", "Own")]
    [ProducesResponseType<ApiResponse<HandoverDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<HandoverDetailDto>>> ApproveHandover(Guid id, CancellationToken cancellationToken)
    {
        var result = await handoverService.ApproveByManagerAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(
            result,
            SuccessMessages.GetMessage(SuccessMessages.HandoverTransferred),
            HttpContext.TraceIdentifier));
    }

    [HttpPost("handovers/{id:guid}/manager/reassign")]
    [HasPermission("handover", "manage", "Own")]
    [ProducesResponseType<ApiResponse<HandoverDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<HandoverDetailDto>>> ReassignHandover(
        Guid id,
        [FromBody] ReassignHandoverRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await handoverService.ReassignByManagerAsync(id, User.GetRequiredUserId(), request, cancellationToken);
        return Ok(ApiResponse.Ok(
            result,
            SuccessMessages.GetMessage(SuccessMessages.HandoverTransferred),
            HttpContext.TraceIdentifier));
    }

    [HttpPost("handovers/{id:guid}/manager/reject")]
    [HasPermission("handover", "manage", "Own")]
    [ProducesResponseType<ApiResponse<HandoverDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<HandoverDetailDto>>> RejectHandoverAsManager(Guid id, CancellationToken cancellationToken)
    {
        var result = await handoverService.RejectByManagerAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(
            result,
            SuccessMessages.GetMessage(SuccessMessages.HandoverRejected),
            HttpContext.TraceIdentifier));
    }
}
