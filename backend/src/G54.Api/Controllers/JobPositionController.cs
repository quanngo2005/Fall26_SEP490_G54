using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("api/v1/job-positions")]
[Authorize]
public sealed class JobPositionController(IJobPositionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<JobPositionDetailDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<JobPositionDetailDto>>>> GetJobPositions(
        [FromQuery] JobPositionFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetJobPositionsAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<JobPositionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobPositionDetailDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy vị trí công việc yêu cầu.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
