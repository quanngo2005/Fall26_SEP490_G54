using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Performance;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Performance evaluation API. Contract: docs/api/PERFORMANCE_EVALUATION_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Route("performance-evaluations")]
[HasPermission("evaluation", "calculate", "Dept")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class PerformanceEvaluationsController(IPerformanceEvaluationService service) : ControllerBase
{
    [HttpPost("{id:guid}/calculate")]
    [ProducesResponseType<ApiResponse<PerformanceEvaluationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<PerformanceEvaluationDto>>> Calculate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.CalculateAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Tính điểm KPI thành công.", HttpContext.TraceIdentifier));
    }
}
