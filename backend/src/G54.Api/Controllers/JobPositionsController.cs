using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Job positions view and details (SCR-ORG-02). Requires the organization:read permission (system admin, branch manager, department manager).
/// Contract: docs/api/JOB_POSITION_CONTRACT.md.
/// </summary>
[ApiController]
[Route("job-positions")]
[HasPermission(OrganizationAccessCodes.Resource, OrganizationAccessCodes.Read, "Dept")]
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

    [HttpGet("{id:guid}/work-types")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<JobPositionWorkTypeDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<JobPositionWorkTypeDto>>>> GetWorkTypes(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetWorkTypesAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/work-types")]
    [HasPermission("job-position", "manage", "Dept")]
    [ProducesResponseType<ApiResponse<JobPositionDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<JobPositionDetailDto>>> AssignWorkType(
        Guid id,
        [FromBody] AssignJobPositionWorkTypeDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.AssignWorkTypeAsync(id, dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Gán loại công việc cho vị trí công việc thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetJobPosition), new { id }, response);
    }

    [HttpDelete("{id:guid}/work-types/{workTypeId:guid}")]
    [HasPermission("job-position", "manage", "Dept")]
    [ProducesResponseType<ApiResponse<JobPositionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobPositionDetailDto>>> RemoveWorkType(
        Guid id,
        Guid workTypeId,
        CancellationToken cancellationToken)
    {
        var result = await service.RemoveWorkTypeAsync(id, workTypeId, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Gỡ loại công việc khỏi vị trí công việc thành công.", HttpContext.TraceIdentifier));
    }
}
