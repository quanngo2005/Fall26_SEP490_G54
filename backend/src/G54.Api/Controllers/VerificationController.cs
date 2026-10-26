using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Verification API. Contract: docs/api/SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Route("verifications")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class VerificationController(IVerificationService service) : ControllerBase
{
    [HttpGet("{submissionId:guid}/dossier")]
    [HasPermission("verification", "decide", "Dept")]
    [ProducesResponseType<ApiResponse<VerificationDossierDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<VerificationDossierDto>>> GetDossier(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDossierAsync(submissionId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Deprecated aliases of GET /verifications/{submissionId}/dossier.</summary>
    [HttpGet("/verification/dossiers/{submissionId:guid}")]
    [HttpGet("pending/{submissionId:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [HasPermission("verification", "decide", "Dept")]
    public Task<ActionResult<ApiResponse<VerificationDossierDto>>> GetDossierLegacy(
        Guid submissionId,
        CancellationToken cancellationToken) => GetDossier(submissionId, cancellationToken);

    [HttpPost("{submissionId:guid}/decision")]
    [HasPermission("verification", "decide", "Dept")]
    [ProducesResponseType<ApiResponse<VerificationDecisionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<VerificationDecisionResultDto>>> SubmitDecision(
        Guid submissionId,
        [FromBody] SubmitVerificationDecisionDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitDecisionAsync(submissionId, User.GetRequiredUserId(), dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, result.Message, HttpContext.TraceIdentifier));
    }

    /// <summary>Deprecated alias of POST /verifications/{submissionId}/decision.</summary>
    [HttpPost("/verification/dossiers/{submissionId:guid}/decision")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [HasPermission("verification", "decide", "Dept")]
    public Task<ActionResult<ApiResponse<VerificationDecisionResultDto>>> SubmitDecisionLegacy(
        Guid submissionId,
        [FromBody] SubmitVerificationDecisionDto dto,
        CancellationToken cancellationToken) => SubmitDecision(submissionId, dto, cancellationToken);

    [HttpGet("{submissionId:guid}/history")]
    [HasPermission("verification", "decide", "Dept")]
    [ProducesResponseType<ApiResponse<List<VerificationHistoryDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<VerificationHistoryDto>>>> GetHistory(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetHistoryAsync(submissionId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Deprecated alias of GET /verifications/{submissionId}/history.</summary>
    [HttpGet("/verification/submissions/{submissionId:guid}/history")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [HasPermission("verification", "decide", "Dept")]
    public Task<ActionResult<ApiResponse<List<VerificationHistoryDto>>>> GetHistoryLegacy(
        Guid submissionId,
        CancellationToken cancellationToken) => GetHistory(submissionId, cancellationToken);
}
