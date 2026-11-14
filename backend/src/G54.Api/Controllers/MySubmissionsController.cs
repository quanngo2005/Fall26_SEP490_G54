using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Submission;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// The caller's own submission history. Contract: docs/api/MY_SUBMISSION_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Route("submissions")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class MySubmissionsController(IMySubmissionService service) : ControllerBase
{
    /// <summary>The latest version of every work the caller submitted, newest first.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<ApiResponse<PagedResult<MySubmissionListItemDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<MySubmissionListItemDto>>>> GetMine(
        [FromQuery] MySubmissionFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetMineAsync(User.GetRequiredUserId(), filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>One submission of the caller with the reconciliation of each transaction code, its versions and appeal window.</summary>
    [HttpGet("{submissionId:guid}/reconciliation")]
    [ProducesResponseType<ApiResponse<MySubmissionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MySubmissionDetailDto>>> GetReconciliation(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDetailAsync(submissionId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
