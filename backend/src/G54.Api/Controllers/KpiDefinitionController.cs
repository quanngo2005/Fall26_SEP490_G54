using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("api/v1/kpi-definitions")]
[Authorize]
public sealed class KpiDefinitionController(IKpiDefinitionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<KpiDefinitionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<KpiDefinitionDto>>>> GetKpis(
        [FromQuery] KpiFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetKpisAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("groups")]
    [ProducesResponseType<ApiResponse<List<KpiGroupOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<KpiGroupOptionDto>>>> GetGroupOptions(CancellationToken cancellationToken)
    {
        var result = await service.GetGroupOptionsAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<KpiDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<KpiDefinitionDto>>> GetKpiById(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetKpiByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy KPI Definition.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<KpiDefinitionDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<KpiDefinitionDto>>> CreateKpi(
        [FromBody] KpiDefinitionSaveDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateKpiAsync(dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Tạo KPI Definition thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetKpiById), new { id = result.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<KpiDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<KpiDefinitionDto>>> UpdateKpi(
        Guid id,
        [FromBody] KpiDefinitionSaveDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateKpiAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật KPI Definition thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> ChangeKpiStatus(
        Guid id,
        [FromBody] KpiStatusDto dto,
        CancellationToken cancellationToken)
    {
        await service.ChangeStatusAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok("Cập nhật trạng thái KPI thành công.", HttpContext.TraceIdentifier));
    }
}
