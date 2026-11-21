using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Read-only target suggestion engine (docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md, D1/D4/D5/D6/D8/D9). Suggests a
/// split for one tier of the existing Target Allocation workflow (docs/api/TARGET_ALLOCATION_CONTRACT.md) from last
/// year's targets or actuals; writes nothing. The caller reviews the result and saves it through the existing
/// quarter-split / allocation / employee-allocation PUT endpoints on <see cref="TargetAssignmentController"/>.
/// </summary>
[ApiController]
[Route("target-suggestions")]
[HasPermission("target", "manage", "Department")]
public sealed class TargetSuggestionController(ITargetSuggestionService service) : ControllerBase
{
    [HttpPost("{year:int}")]
    [ProducesResponseType<ApiResponse<SuggestTargetsResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SuggestTargetsResponseDto>>> Suggest(
        int year,
        [FromBody] SuggestTargetsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.SuggestAsync(year, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
