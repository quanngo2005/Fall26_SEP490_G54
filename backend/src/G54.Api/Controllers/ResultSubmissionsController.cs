using System.Security.Claims;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Submission;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ResultSubmissionsController(IResultSubmissionService submissionService) : ControllerBase
{
    [HttpGet("api/v1/works/{workId:guid}/submission-context")]
    [ProducesResponseType<ApiResponse<SubmissionContextDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubmissionContextDto>>> GetSubmissionContext(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await submissionService.GetSubmissionContextAsync(workId, userId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("api/v1/works/{workId:guid}/submissions")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ApiResponse<SubmissionResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubmissionResultDto>>> CreateSubmission(
        Guid workId,
        [FromForm] CreateWorkSubmissionRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await submissionService.CreateSubmissionAsync(workId, userId, request, cancellationToken);
        var response = ApiResponse.Ok(
            result,
            "Báo cáo kết quả công việc đã được gửi thành công!",
            HttpContext.TraceIdentifier);

        return CreatedAtAction(
            nameof(GetSubmissionById),
            new { submissionId = result.SubmissionId },
            response);
    }

    [HttpGet("api/v1/works/{workId:guid}/submissions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SubmissionDetailDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SubmissionDetailDto>>>> GetSubmissionsByWorkId(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await submissionService.GetSubmissionsByWorkIdAsync(workId, userId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("api/v1/works/{workId:guid}/submissions/{submissionId:guid}")]
    [HttpGet("api/v1/submissions/{submissionId:guid}")]
    [ProducesResponseType<ApiResponse<SubmissionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubmissionDetailDto>>> GetSubmissionById(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await submissionService.GetSubmissionByIdAsync(submissionId, userId, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy thông tin đợt nộp báo cáo kết quả.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("api/v1/works/{workId:guid}/submissions/{submissionId:guid}/withdraw")]
    [HttpPost("api/v1/submissions/{submissionId:guid}/withdraw")]
    [ProducesResponseType<ApiResponse<SubmissionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubmissionResultDto>>> WithdrawSubmission(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await submissionService.WithdrawSubmissionAsync(submissionId, userId, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Thu hồi báo cáo kết quả công việc thành công.", HttpContext.TraceIdentifier));
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
        {
            throw new UnauthorizedException("Không xác định được danh tính người dùng.");
        }

        return userId;
    }
}
