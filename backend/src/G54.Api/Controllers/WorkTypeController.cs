using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.WorkType;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("work-types")]
[Authorize]
public sealed class WorkTypeController(IWorkTypeService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<WorkTypeDetailDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<WorkTypeDetailDto>>>> GetWorkTypes(
        [FromQuery] WorkTypeFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetWorkTypesAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("kpis")]
    [HttpGet("/kpis")]
    [ProducesResponseType<ApiResponse<List<KpiOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<KpiOptionDto>>>> GetActiveKpis(CancellationToken cancellationToken)
    {
        var result = await service.GetActiveKpisAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("workflows")]
    [HttpGet("/workflows")]
    [ProducesResponseType<ApiResponse<List<WorkflowOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<WorkflowOptionDto>>>> GetWorkflows(CancellationToken cancellationToken)
    {
        var result = await service.GetWorkflowsAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<WorkTypeDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkTypeDetailDto>>> GetWorkTypeById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetWorkTypeByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy loại công việc yêu cầu.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [HasPermission("work-type", "manage", "All")]
    [ProducesResponseType<ApiResponse<WorkTypeDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<WorkTypeDetailDto>>> CreateWorkType(
        [FromBody] CreateWorkTypeDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateWorkTypeAsync(dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Tạo loại công việc và cấu hình KPI thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetWorkTypeById), new { id = result.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("work-type", "manage", "All")]
    [ProducesResponseType<ApiResponse<WorkTypeDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<WorkTypeDetailDto>>> UpdateWorkType(
        Guid id,
        [FromBody] UpdateWorkTypeDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateWorkTypeAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật loại công việc thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission("work-type", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> ChangeWorkTypeStatus(
        Guid id,
        [FromBody] WorkTypeStatusDto dto,
        CancellationToken cancellationToken)
    {
        await service.ChangeStatusAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok("Cập nhật trạng thái loại công việc thành công.", HttpContext.TraceIdentifier));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission("work-type", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> DeleteWorkType(
        Guid id,
        CancellationToken cancellationToken)
    {
        await service.DeleteWorkTypeAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok("Xóa loại công việc thành công.", HttpContext.TraceIdentifier));
    }
}
