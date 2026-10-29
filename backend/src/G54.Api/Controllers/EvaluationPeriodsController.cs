using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.EvaluationPeriod;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Evaluation period lifecycle: create (OPEN) and close (OPEN → IN_REVIEW). Locking stays on
/// <c>POST /evaluations/approvals/finalize</c>. Contract: docs/api/EVALUATION_PERIOD_CONTRACT.md.
/// </summary>
[ApiController]
[Route("evaluation-periods")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class EvaluationPeriodsController(IEvaluationPeriodService service) : ControllerBase
{
    [HttpPost]
    [HasPermission("evaluation", "finalize", PermissionScopes.Branch)]
    [ProducesResponseType<ApiResponse<EvaluationPeriodStatusDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EvaluationPeriodStatusDto>>> CreatePeriod(
        [FromBody] CreateEvaluationPeriodRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        var response = new ApiResponse<EvaluationPeriodStatusDto>
        {
            Success = true,
            Status = StatusCodes.Status201Created,
            Data = result,
            Message = SuccessMessages.GetMessage(SuccessMessages.Created),
            TraceId = HttpContext.TraceIdentifier,
        };
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{id:guid}/close")]
    [HasPermission("evaluation", "finalize", PermissionScopes.Branch)]
    [ProducesResponseType<ApiResponse<ClosePeriodResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ClosePeriodResultDto>>> ClosePeriod(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.CloseAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
