using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Employee-facing "my-targets" routes for Target Allocation v3 (BR-36): own year/quarter targets, year-level
/// confirmation, feedback with an optional proposed value, DM reply, and quarter progress.
/// Deliberately [Authorize]-only at the controller level (like <see cref="MyTargetAssignmentsController"/>): the
/// caller's employeeId is always derived server-side, so any authenticated employee can see/confirm their own
/// data. The DM reply action additionally requires target:manage at Department scope.
/// Contract: docs/api/TARGET_ALLOCATION_CONTRACT.md "Employee side".
/// </summary>
[ApiController]
[Route("my-targets")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class MyTargetsController(IMyTargetsService service) : ControllerBase
{
    /// <summary>The caller's own KPI targets for the year (year + 4 quarters per KPI, ack status, changed KPIs).</summary>
    [HttpGet("{year:int}")]
    [ProducesResponseType<ApiResponse<MyTargetsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MyTargetsDto>>> GetMyTargets(int year, CancellationToken cancellationToken)
    {
        var result = await service.GetMyTargetsAsync(User.GetRequiredUserId(), year, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Confirms (or reconfirms) the caller's targets for the year.</summary>
    [HttpPost("{year:int}/confirm")]
    [ProducesResponseType<ApiResponse<ConfirmMyTargetsResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ConfirmMyTargetsResultDto>>> Confirm(int year, CancellationToken cancellationToken)
    {
        var result = await service.ConfirmAsync(User.GetRequiredUserId(), year, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Xác nhận chỉ tiêu thành công.", HttpContext.TraceIdentifier));
    }

    /// <summary>Submits feedback (and an optional proposed value) on one of the caller's own KPIs.</summary>
    [HttpPost("{year:int}/feedback")]
    [ProducesResponseType<ApiResponse<TargetFeedbackDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<TargetFeedbackDto>>> SubmitFeedback(
        int year, [FromBody] SubmitTargetFeedbackRequest request, CancellationToken cancellationToken)
    {
        var result = await service.SubmitFeedbackAsync(User.GetRequiredUserId(), year, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse.Ok(result, "Gửi góp ý thành công.", HttpContext.TraceIdentifier));
    }

    /// <summary>DM reply to an employee's feedback; scoped to the DM's own department.</summary>
    [HttpPost("feedback/{feedbackId:guid}/reply")]
    [HasPermission("target", "manage", PermissionScopes.Dept)]
    [ProducesResponseType<ApiResponse<TargetFeedbackDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TargetFeedbackDto>>> ReplyFeedback(
        Guid feedbackId, [FromBody] ReplyTargetFeedbackRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ReplyFeedbackAsync(User.GetRequiredUserId(), feedbackId, request, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Đã gửi phản hồi.", HttpContext.TraceIdentifier));
    }

    /// <summary>Quarter/year progress for the caller: achieved counts only auto-reconciled results, with the
    /// still-pending (IN_REVIEW) count reported separately, plus expected pace (D16).</summary>
    [HttpGet("{year:int}/progress")]
    [ProducesResponseType<ApiResponse<MyTargetsProgressDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MyTargetsProgressDto>>> GetProgress(
        int year, [FromQuery] string? period, CancellationToken cancellationToken)
    {
        var result = await service.GetProgressAsync(User.GetRequiredUserId(), year, period, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>DM listing of employee feedback for a department/year (W9 panel, W11 drawer), optionally narrowed to one employee.</summary>
    [HttpGet("{year:int}/feedback")]
    [HasPermission("target", "manage", PermissionScopes.Dept)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetFeedbackDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetFeedbackDto>>>> GetFeedback(
        int year, [FromQuery] Guid departmentId, [FromQuery] Guid? employeeId, CancellationToken cancellationToken)
    {
        var result = await service.GetFeedbackAsync(User.GetRequiredUserId(), year, departmentId, employeeId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Change-log history for one employee/year (W11 drawer), optionally narrowed to one KPI. Allowed for the employee themselves, or their DM.</summary>
    [HttpGet("{year:int}/change-log")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetChangeLogEntryDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetChangeLogEntryDto>>>> GetChangeLog(
        int year, [FromQuery] Guid employeeId, [FromQuery] Guid? kpiId, CancellationToken cancellationToken)
    {
        var result = await service.GetChangeLogAsync(User.GetRequiredUserId(), year, employeeId, kpiId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>The caller's own feedback history (with any DM reply), optionally narrowed to one KPI (E5).</summary>
    [HttpGet("{year:int}/my-feedback")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetFeedbackDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetFeedbackDto>>>> GetMyFeedback(
        int year, [FromQuery] Guid? kpiId, CancellationToken cancellationToken)
    {
        var result = await service.GetMyFeedbackAsync(User.GetRequiredUserId(), year, kpiId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>The reconciled (or still-pending) transactions behind the caller's own result for one KPI/year (E5).</summary>
    [HttpGet("{year:int}/kpis/{kpiId:guid}/contributions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MyContributionDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MyContributionDto>>>> GetMyContributions(
        int year, Guid kpiId, CancellationToken cancellationToken)
    {
        var result = await service.GetMyContributionsAsync(User.GetRequiredUserId(), year, kpiId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
