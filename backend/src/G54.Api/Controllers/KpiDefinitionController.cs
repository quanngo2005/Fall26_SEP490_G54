using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("kpi-definitions")]
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

    [HttpGet("code-suggestion")]
    [HasPermission("kpi", "read", "All")]
    [ProducesResponseType<ApiResponse<KpiCodeSuggestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<KpiCodeSuggestionDto>>> SuggestCode([FromQuery] string? name, CancellationToken cancellationToken)
    {
        var result = await service.SuggestCodeAsync(name, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("summary")]
    [HasPermission("kpi", "read", "All")]
    [ProducesResponseType<ApiResponse<KpiSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<KpiSummaryDto>>> GetSummary(CancellationToken cancellationToken)
    {
        var result = await service.GetSummaryAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}/usage")]
    [HasPermission("kpi", "read", "All")]
    [ProducesResponseType<ApiResponse<KpiUsageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<KpiUsageDto>>> GetUsage(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetUsageAsync(id, cancellationToken) ?? throw new NotFoundException("KPI Definition", id);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}/history")]
    [HasPermission("kpi", "read", "All")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<KpiHistoryEntryDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<KpiHistoryEntryDto>>>> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetHistoryAsync(id, cancellationToken) ?? throw new NotFoundException("KPI Definition", id);
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
    [HasPermission("kpi", "manage", "All")]
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

    [HttpPost("requests")]
    [HasPermission("kpi", "request", "All")]
    [ProducesResponseType<ApiResponse<KpiDefinitionDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<KpiDefinitionDto>>> RequestKpi(
        [FromBody] KpiRequestDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestKpiAsync(User.GetRequiredUserId(), dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Gửi yêu cầu bổ sung KPI thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetKpiById), new { id = result.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("kpi", "manage", "All")]
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
    [HasPermission("kpi", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> ChangeKpiStatus(
        Guid id,
        [FromBody] KpiStatusDto dto,
        CancellationToken cancellationToken)
    {
        await service.ChangeStatusAsync(User.GetRequiredUserId(), id, dto, cancellationToken);
        return Ok(ApiResponse.Ok("Cập nhật trạng thái KPI thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission("kpi", "approve", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse>> ApproveKpi(Guid id, CancellationToken cancellationToken)
    {
        await service.ApproveKpiAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok("Duyệt KPI thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission("kpi", "approve", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse>> RejectKpi(
        Guid id,
        [FromBody] RejectKpiDto dto,
        CancellationToken cancellationToken)
    {
        await service.RejectKpiAsync(User.GetRequiredUserId(), id, dto, cancellationToken);
        return Ok(ApiResponse.Ok("Từ chối KPI thành công.", HttpContext.TraceIdentifier));
    }
}
