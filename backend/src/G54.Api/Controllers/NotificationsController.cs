using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Notification;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Announcements and each employee's notification inbox. Contract: docs/api/NOTIFICATION_CONTRACT.md.
/// </summary>
[ApiController]
[Authorize]
[Route("notifications")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class NotificationsController(INotificationService service) : ControllerBase
{
    [HttpPost]
    [HasPermission("notification", "send", "Dept")]
    [ProducesResponseType<ApiResponse<SendNotificationResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SendNotificationResultDto>>> Send(
        [FromBody] SendNotificationDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.SendAsync(User.GetRequiredUserId(), request, cancellationToken);
        var response = ApiResponse.Ok(result, "Gửi thông báo thành công.", HttpContext.TraceIdentifier);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    [HasPermission("notification", "read", "Own")]
    [ProducesResponseType<ApiResponse<PagedResult<NotificationDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> GetMine(
        [FromQuery] NotificationFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetMineAsync(User.GetRequiredUserId(), filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("unread-count")]
    [HasPermission("notification", "read", "Own")]
    [ProducesResponseType<ApiResponse<NotificationCountDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<NotificationCountDto>>> GetUnreadCount(CancellationToken cancellationToken)
    {
        var result = await service.GetUnreadCountAsync(User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/read")]
    [HasPermission("notification", "read", "Own")]
    [ProducesResponseType<ApiResponse<NotificationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<NotificationDto>>> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.MarkReadAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("read-all")]
    [HasPermission("notification", "read", "Own")]
    [ProducesResponseType<ApiResponse<NotificationCountDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<NotificationCountDto>>> MarkAllRead(CancellationToken cancellationToken)
    {
        var result = await service.MarkAllReadAsync(User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
