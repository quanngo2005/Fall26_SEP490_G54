using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Appeal;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Appeal API. Contract: docs/api/APPEAL_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Authorize]
[Route("")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class AppealsController(IAppealService appealService) : ControllerBase
{
    /// <summary>10 files of up to 25 MB plus room for the other form fields; raised only for the appeal upload.</summary>
    private const long MaxAppealRequestBytes = (10 * StorageService.DefaultMaxFileSizeBytes) + (1024 * 1024);

    [HttpPost("submissions/{submissionId:guid}/appeals")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAppealRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAppealRequestBytes)]
    [ProducesResponseType<ApiResponse<AppealDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<AppealDetailDto>>> CreateAppeal(
        Guid submissionId,
        [FromForm] CreateAppealRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await appealService.CreateAppealAsync(submissionId, User.GetRequiredUserId(), request, cancellationToken);
        var response = ApiResponse.Ok(
            result,
            ErrorCodes.GetDefaultMessage(ErrorCodes.MSG04),
            HttpContext.TraceIdentifier);

        return CreatedAtAction(nameof(GetAppealById), new { id = result.Id }, response);
    }

    [HttpPost("appeals/{id:guid}/withdraw")]
    [ProducesResponseType<ApiResponse<AppealDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AppealDetailDto>>> WithdrawAppeal(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await appealService.WithdrawAppealAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("appeals")]
    [ProducesResponseType<ApiResponse<PagedResult<AppealListItemDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<AppealListItemDto>>>> GetAppeals(
        [FromQuery] AppealFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await appealService.GetAppealsAsync(User.GetRequiredUserId(), filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("appeals/{id:guid}")]
    [ProducesResponseType<ApiResponse<AppealDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AppealDetailDto>>> GetAppealById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await appealService.GetAppealByIdAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
