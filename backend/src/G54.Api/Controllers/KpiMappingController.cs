using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("business-objectives/{objectiveId:guid}/kpis")]
[Authorize]
public sealed class KpiMappingController(IKpiMappingService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<List<ObjectiveKpiDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ObjectiveKpiDto>>>> GetObjectiveKpis(
        Guid objectiveId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetObjectiveKpisAsync(objectiveId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("available")]
    [ProducesResponseType<ApiResponse<List<KpiOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<KpiOptionDto>>>> GetAvailableKpis(CancellationToken cancellationToken)
    {
        var result = await service.GetAvailableKpisAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<ObjectiveKpiDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ObjectiveKpiDto>>> AddObjectiveKpi(
        Guid objectiveId,
        [FromBody] ObjectiveKpiSaveDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.AddObjectiveKpiAsync(objectiveId, dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Liên kết KPI với mục tiêu kinh doanh thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetObjectiveKpis), new { objectiveId }, response);
    }

    [HttpDelete("{kpiId:guid}")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> RemoveObjectiveKpi(
        Guid objectiveId,
        Guid kpiId,
        CancellationToken cancellationToken)
    {
        await service.RemoveObjectiveKpiAsync(objectiveId, kpiId, cancellationToken);
        return Ok(ApiResponse.Ok("Gỡ KPI khỏi mục tiêu kinh doanh thành công.", HttpContext.TraceIdentifier));
    }
}
