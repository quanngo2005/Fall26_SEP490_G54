using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Submission;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Result submission API. Contract: docs/api/SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Authorize]
[Route("")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class ResultSubmissionsController(IResultSubmissionService submissionService) : ControllerBase
{
    /// <summary>10 files of up to 25 MB plus room for the other form fields; raised only for the submission upload.</summary>
    private const long MaxSubmissionRequestBytes = (10 * StorageService.DefaultMaxFileSizeBytes) + (1024 * 1024);

    [HttpGet("works/{workId:guid}/submission-context")]
    [ProducesResponseType<ApiResponse<SubmissionContextDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubmissionContextDto>>> GetSubmissionContext(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var result = await submissionService.GetSubmissionContextAsync(workId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("works/{workId:guid}/submissions")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxSubmissionRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSubmissionRequestBytes)]
    [ProducesResponseType<ApiResponse<SubmissionResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SubmissionResultDto>>> CreateSubmission(
        Guid workId,
        [FromForm] CreateWorkSubmissionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await submissionService.CreateSubmissionAsync(workId, User.GetRequiredUserId(), request, cancellationToken);
        var response = ApiResponse.Ok(
            result,
            ErrorCodes.GetDefaultMessage(ErrorCodes.MSG06),
            HttpContext.TraceIdentifier);

        return CreatedAtAction(nameof(GetSubmissionById), new { submissionId = result.SubmissionId }, response);
    }

    [HttpGet("works/{workId:guid}/submissions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SubmissionDetailDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SubmissionDetailDto>>>> GetSubmissionsByWorkId(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var result = await submissionService.GetSubmissionsByWorkIdAsync(workId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("submissions/{submissionId:guid}")]
    [ProducesResponseType<ApiResponse<SubmissionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubmissionDetailDto>>> GetSubmissionById(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await submissionService.GetSubmissionByIdAsync(submissionId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Deprecated alias of GET /submissions/{submissionId}; 404 when the submission belongs to another work.</summary>
    [HttpGet("works/{workId:guid}/submissions/{submissionId:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<ApiResponse<SubmissionDetailDto>>> GetSubmissionByIdLegacy(
        Guid workId,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await submissionService.GetSubmissionByIdAsync(submissionId, User.GetRequiredUserId(), cancellationToken);
        if (result.WorkId != workId)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy đợt nộp kết quả.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("submissions/{submissionId:guid}/withdraw")]
    [ProducesResponseType<ApiResponse<SubmissionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SubmissionResultDto>>> WithdrawSubmission(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await submissionService.WithdrawSubmissionAsync(submissionId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Thu hồi báo cáo kết quả công việc thành công.", HttpContext.TraceIdentifier));
    }

    /// <summary>Deprecated alias of POST /submissions/{submissionId}/withdraw.</summary>
    [HttpPost("works/{workId:guid}/submissions/{submissionId:guid}/withdraw")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<ActionResult<ApiResponse<SubmissionResultDto>>> WithdrawSubmissionLegacy(
        Guid submissionId,
        CancellationToken cancellationToken) => WithdrawSubmission(submissionId, cancellationToken);
}
