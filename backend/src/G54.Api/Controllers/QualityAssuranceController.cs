using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Quality;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// QA quality control: flag an approved result, request rework, and re-verify the random 5% sample (BR-32).
/// Contract: docs/api/QUALITY_ASSURANCE_CONTRACT.md. Errors are raised as AppException and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class QualityAssuranceController(IQualityAssuranceService service) : ControllerBase
{
    [HttpPost("submissions/{submissionId:guid}/quality-flags")]
    [HasPermission("quality_flag", "create", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<QualityFlagDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<QualityFlagDto>>> CreateFlag(
        Guid submissionId,
        [FromBody] CreateQualityFlagDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateFlagAsync(submissionId, User.GetRequiredUserId(), dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Đã gắn cờ chất lượng.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetFlag), new { id = result.Id }, response);
    }

    [HttpGet("quality-flags")]
    [HasPermission("quality_flag", "read", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<PagedResult<QualityFlagDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<QualityFlagDto>>>> GetFlags(
        [FromQuery] QualityFlagFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetFlagsAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("quality-flags/{id:guid}")]
    [HasPermission("quality_flag", "read", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<QualityFlagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<QualityFlagDto>>> GetFlag(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetFlagAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("quality-flags/{id:guid}/request-rework")]
    [HasPermission("quality_flag", "rework", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<QualityFlagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<QualityFlagDto>>> RequestRework(
        Guid id,
        [FromBody] RequestReworkDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestReworkAsync(id, User.GetRequiredUserId(), dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Đã yêu cầu nhân viên làm lại công việc.", HttpContext.TraceIdentifier));
    }

    [HttpPost("quality-flags/{id:guid}/dismiss")]
    [HasPermission("quality_flag", "dismiss", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<QualityFlagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<QualityFlagDto>>> DismissFlag(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.DismissFlagAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("qa-samples")]
    [HasPermission("qa_sample", "read", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<PagedResult<QaSampleDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<QaSampleDto>>>> GetSamples(
        [FromQuery] QaSampleFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetSamplesAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("qa-samples/{id:guid}/decision")]
    [HasPermission("qa_sample", "decide", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<QaSampleDecisionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<QaSampleDecisionDto>>> DecideSample(
        Guid id,
        [FromBody] DecideQaSampleDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.DecideSampleAsync(id, User.GetRequiredUserId(), dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Đã ghi nhận kết quả kiểm tra QA.", HttpContext.TraceIdentifier));
    }
}
