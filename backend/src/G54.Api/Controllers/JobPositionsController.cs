using G54.Api.Authorization;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Job positions view and details (SCR-ORG-02). Restricted to system admin, branch manager and department manager.
/// Contract: docs/api/JOB_POSITION_CONTRACT.md.
/// </summary>
[ApiController]
[Route("job-positions")]
[Authorize(Policy = OrganizationAccess.Policy)]
public sealed class JobPositionsController(IJobPositionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<JobPositionSummaryDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<JobPositionSummaryDto>>>> GetJobPositions(
        [FromQuery] JobPositionFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetJobPositionsAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<JobPositionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobPositionDetailDto>>> GetJobPosition(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetJobPositionByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Vị trí công việc", id);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
