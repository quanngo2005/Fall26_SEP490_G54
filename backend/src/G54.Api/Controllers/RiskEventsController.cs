using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.RiskEvent;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Risk event API. Contract: docs/api/RISK_EVENT_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Route("risk-events")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class RiskEventsController(IRiskEventService service) : ControllerBase
{
    [HttpPost]
    [HasPermission("risk_event", "create", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RiskEventDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RiskEventDto>>> CreateRiskEvent(
        [FromBody] CreateRiskEventRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, User.GetRequiredUserId(), cancellationToken);
        var response = new ApiResponse<RiskEventDto>
        {
            Success = true,
            Status = StatusCodes.Status201Created,
            Data = result,
            Message = SuccessMessages.GetMessage(SuccessMessages.Created),
            TraceId = HttpContext.TraceIdentifier,
        };
        return CreatedAtAction(nameof(GetRiskEvent), new { id = result.Id }, response);
    }

    [HttpGet("{id:guid}")]
    [HasPermission("risk_event", "read", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RiskEventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RiskEventDto>>> GetRiskEvent(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/confirm")]
    [HasPermission("risk_event", "confirm", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RiskEventConfirmationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RiskEventConfirmationDto>>> ConfirmRiskEvent(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.ConfirmAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/dismiss")]
    [HasPermission("risk_event", "dismiss", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RiskEventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RiskEventDto>>> DismissRiskEvent(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.DismissAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/resolve")]
    [HasPermission("risk_event", "resolve", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RiskEventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RiskEventDto>>> ResolveRiskEvent(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.ResolveAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
