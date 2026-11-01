using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Employee-facing target assignment routes: own targets, breakdown, confirm/comment feedback.
/// Deliberately [Authorize]-only (not [HasPermission]): the caller's employeeId is always derived
/// server-side from the authenticated account, never trusted from the request, so no extra
/// resource-scoped permission needs to be seeded for employees (Teller/SO) to use their own data.
/// Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.
/// </summary>
[ApiController]
[Route("target-assignments")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class MyTargetAssignmentsController(ITargetAssignmentService service) : ControllerBase
{
    /// <summary>The caller's own employee-owned target assignments, with feedbackStatus.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>> GetMine([FromQuery] MyTargetAssignmentQueryDto query)
    {
        var result = await service.GetMineAsync(User.GetRequiredUserId(), query);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Working-day breakdown (MONTH/WEEK/DAY) of a target's period. Read-only, computed, not stored.
    /// Readable by the owning employee or anyone passing the service's org-scope check.</summary>
    [HttpGet("{id:guid}/breakdown")]
    [ProducesResponseType<ApiResponse<TargetAssignmentBreakdownDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TargetAssignmentBreakdownDto>>> GetBreakdown(Guid id, [FromQuery] string? granularity)
    {
        var result = await service.GetBreakdownAsync(User.GetRequiredUserId(), id, granularity);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Owner employee confirms the target (once per update).</summary>
    // Not "{id}/confirm": TargetAssignmentController already owns POST target-assignments/{deptYearId}/confirm.
    [HttpPost("{id:guid}/feedback/confirm")]
    [ProducesResponseType<ApiResponse<TargetAssignmentFeedbackDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TargetAssignmentFeedbackDto>>> Confirm(Guid id)
    {
        var result = await service.ConfirmAsync(User.GetRequiredUserId(), id);
        return StatusCode(StatusCodes.Status201Created, Created(result, "Xác nhận target thành công."));
    }

    /// <summary>Owner employee leaves a comment (&lt;= 500 chars) on the target.</summary>
    [HttpPost("{id:guid}/feedback/comments")]
    [ProducesResponseType<ApiResponse<TargetAssignmentFeedbackDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TargetAssignmentFeedbackDto>>> Comment(Guid id, [FromBody] CreateTargetFeedbackCommentDto dto)
    {
        var result = await service.CommentAsync(User.GetRequiredUserId(), id, dto);
        return StatusCode(StatusCodes.Status201Created, Created(result, "Gửi góp ý thành công."));
    }

    /// <summary>Feedback history for a target, newest first. Owner employee or org-scoped caller only.</summary>
    [HttpGet("{id:guid}/feedback")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetAssignmentFeedbackDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetAssignmentFeedbackDto>>>> GetFeedback(Guid id)
    {
        var result = await service.GetFeedbackAsync(User.GetRequiredUserId(), id);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    // ApiResponse.Ok fixes the envelope status at 200; keep it in step with the 201 HTTP status.
    private ApiResponse<T> Created<T>(T data, string message) => new()
    {
        Success = true,
        Status = StatusCodes.Status201Created,
        Data = data,
        Message = message,
        TraceId = HttpContext.TraceIdentifier,
    };
}
