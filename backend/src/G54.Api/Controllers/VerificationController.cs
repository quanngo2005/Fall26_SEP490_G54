using System.Security.Claims;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("api/v1/verification")]
[Authorize]
public sealed class VerificationController(IVerificationService service) : ControllerBase
{
    [HttpGet("dossiers/{submissionId:guid}")]
    [HttpGet("/api/v1/verifications/pending/{submissionId:guid}")]
    [ProducesResponseType<ApiResponse<VerificationDossierDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<VerificationDossierDto>>> GetDossier(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDossierAsync(submissionId, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy hồ sơ nộp kết quả công việc yêu cầu thẩm định.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("dossiers/{submissionId:guid}/decision")]
    [HttpPost("/api/v1/verifications/{submissionId:guid}/decision")]
    [ProducesResponseType<ApiResponse<VerificationDecisionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<VerificationDecisionResultDto>>> SubmitDecision(
        Guid submissionId,
        [FromBody] SubmitVerificationDecisionDto dto,
        CancellationToken cancellationToken)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue)
        {
            return Unauthorized(ApiResponse.Fail(
                ErrorCodes.Unauthorized,
                "Không xác định được danh tính người dùng từ token xác thực.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status401Unauthorized));
        }

        var result = await service.SubmitDecisionAsync(
            submissionId,
            accountId.Value,
            dto,
            cancellationToken);

        return Ok(ApiResponse.Ok(result, result.Message, HttpContext.TraceIdentifier));
    }

    [HttpGet("submissions/{submissionId:guid}/history")]
    [HttpGet("/api/v1/verifications/{submissionId:guid}/history")]
    [ProducesResponseType<ApiResponse<List<VerificationHistoryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<VerificationHistoryDto>>>> GetHistory(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetHistoryAsync(submissionId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    private Guid? GetCurrentAccountId()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(sub, out var accountId) ? accountId : null;
    }
}
