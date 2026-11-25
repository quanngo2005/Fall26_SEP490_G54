using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.CallRecording;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Call recordings of a result submission. Contract: docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md §2–§4. Access (submitter, or
/// <c>call_recording:play</c> at All) is decided by <see cref="ICallRecordingService"/>; errors are raised as AppException and
/// mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Authorize]
[Route("")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class CallRecordingsController(ICallRecordingService callRecordingService) : ControllerBase
{
    /// <summary>One recording plus room for the other form fields.</summary>
    private const long MaxUploadRequestBytes = CallRecordingOptions.MaxUploadBytes + (1024 * 1024);

    [HttpPost("submissions/{submissionId:guid}/call-recordings")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<ApiResponse<CallRecordingDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CallRecordingDto>>> Upload(
        Guid submissionId,
        [FromForm] UploadCallRecordingRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await callRecordingService.UploadAsync(submissionId, User.GetRequiredUserId(), request, cancellationToken);
        var response = new ApiResponse<CallRecordingDto>
        {
            Success = true,
            Status = StatusCodes.Status201Created,
            Data = result,
            TraceId = HttpContext.TraceIdentifier,
        };
        return CreatedAtAction(nameof(GetCallRecording), new { id = result.Id }, response);
    }

    [HttpGet("submissions/{submissionId:guid}/call-recordings")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CallRecordingDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CallRecordingDto>>>> GetBySubmission(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await callRecordingService.GetBySubmissionAsync(submissionId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("call-recordings/{id:guid}")]
    [ProducesResponseType<ApiResponse<CallRecordingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CallRecordingDto>>> GetCallRecording(Guid id, CancellationToken cancellationToken)
    {
        var result = await callRecordingService.GetAsync(id, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>The muted audio with <c>Accept-Ranges: bytes</c>; a <c>Range</c> request is answered with 206.</summary>
    [HttpGet("call-recordings/{id:guid}/stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Stream(Guid id, CancellationToken cancellationToken)
    {
        var audio = await callRecordingService.OpenStreamAsync(id, User.GetRequiredUserId(), cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        return File(audio.Content, audio.ContentType, enableRangeProcessing: true);
    }
}
