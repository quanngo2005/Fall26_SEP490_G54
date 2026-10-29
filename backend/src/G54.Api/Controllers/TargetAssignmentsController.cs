using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>Branch / department / employee target allocation. Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.</summary>
[ApiController]
[Route("target-assignments")]
[HasPermission("target", "manage", "Dept")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class TargetAssignmentsController(ITargetAssignmentService service) : ControllerBase
{
    /// <summary>Assigns the root target of a KPI and period to a branch (administrators only).</summary>
    [HttpPost]
    [ProducesResponseType<ApiResponse<TargetAssignmentDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<TargetAssignmentDto>>> CreateRoot([FromBody] CreateTargetAssignmentDto dto)
    {
        var result = await service.CreateRootAsync(User.GetRequiredUserId(), dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, Created(result, "Giao target cho chi nhánh thành công."));
    }

    /// <summary>Splits an ACTIVE target across the level directly below its owner (by organization).</summary>
    [HttpPost("{id:guid}/allocations")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>> Allocate(
        Guid id,
        [FromBody] AllocateTargetDto dto)
    {
        var result = await service.AllocateAsync(User.GetRequiredUserId(), id, dto);
        return CreatedAtAction(nameof(GetById), new { id }, Created(result, "Phân bổ target thành công."));
    }

    /// <summary>Splits an ACTIVE target across evaluation periods (by period).</summary>
    [HttpPost("{id:guid}/period-allocations")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>> AllocateByPeriod(
        Guid id,
        [FromBody] AllocateTargetByPeriodDto dto)
    {
        var result = await service.AllocateByPeriodAsync(User.GetRequiredUserId(), id, dto);
        return CreatedAtAction(nameof(GetById), new { id }, Created(result, "Phân bổ target theo kỳ thành công."));
    }

    /// <summary>Lists target assignments with optional filters.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>> List([FromQuery] TargetAssignmentQueryDto query)
    {
        var result = await service.ListAsync(User.GetRequiredUserId(), query);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<TargetAssignmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TargetAssignmentDto>>> GetById(Guid id)
    {
        var result = await service.GetAsync(User.GetRequiredUserId(), id);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    // ApiResponse.Ok fixes the envelope status at 200; keep it in step with the 201 HTTP status.
    private ApiResponse<T> Created<T>(T data, string message) => new()
    {
        Success = true,
        Status = StatusCodes.Status201Created,
        Data = data,
        Message = message,
        TraceId = HttpContext.TraceIdentifier,
    };
}
