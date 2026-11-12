using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Ranking records of locked evaluation periods for HR (UC110, UC111). Contract: docs/api/HR_RANKING_RECORD_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Route("ranking-records")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class RankingRecordsController(IRankingRecordService service) : ControllerBase
{
    /// <summary>Lists the ranking records of one locked period, filtered by department, job position, tier or keyword.</summary>
    [HttpGet]
    [HasPermission("ranking_record", "read", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<PagedResult<RankingRecordDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<PagedResult<RankingRecordDto>>>> GetRecords(
        [FromQuery] RankingRecordFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetRecordsAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Gets one ranking record with its period and review information.</summary>
    [HttpGet("{evaluationId:guid}")]
    [HasPermission("ranking_record", "read", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RankingRecordDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RankingRecordDetailDto>>> GetRecord(
        Guid evaluationId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetRecordAsync(evaluationId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
