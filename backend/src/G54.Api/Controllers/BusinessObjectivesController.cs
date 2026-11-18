using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.BusinessObjective;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("business-objectives")]
public sealed class BusinessObjectivesController(IBusinessObjectiveService service) : ControllerBase
{
    [HttpGet]
    [HasPermission("objective", "read", "Dept")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BusinessObjectiveDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusinessObjectiveDto>>>> GetObjectives(
        [FromQuery] BusinessObjectiveFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetObjectivesAsync(User.GetRequiredUserId(), filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [HasPermission("objective", "read", "Dept")]
    [ProducesResponseType<ApiResponse<BusinessObjectiveDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BusinessObjectiveDetailDto>>> GetObjective(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetObjectiveAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [HasPermission("objective", "manage", "All")]
    [ProducesResponseType<ApiResponse<BusinessObjectiveDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BusinessObjectiveDto>>> CreateObjective(
        [FromBody] BusinessObjectiveSaveDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateObjectiveAsync(dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Tạo mục tiêu kinh doanh thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetObjective), new { id = result.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("objective", "manage", "All")]
    [ProducesResponseType<ApiResponse<BusinessObjectiveDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BusinessObjectiveDto>>> UpdateObjective(
        Guid id,
        [FromBody] BusinessObjectiveSaveDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateObjectiveAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật mục tiêu kinh doanh thành công.", HttpContext.TraceIdentifier));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission("objective", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> DeleteObjective(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteObjectiveAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok("Xóa mục tiêu kinh doanh thành công.", HttpContext.TraceIdentifier));
    }
}
