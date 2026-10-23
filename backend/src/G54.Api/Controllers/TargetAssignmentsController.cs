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
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TargetAssignmentDto>>> CreateRoot(
        [FromBody] CreateTargetAssignmentDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateRootAsync(User.GetRequiredUserId(), dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, CreatedEnvelope(result, "Giao target cho chi nhánh thành công."));
    }

    /// <summary>Splits an ACTIVE department target across the level directly below it.</summary>
    [HttpPost("{id:guid}/allocations")]
    [ProducesResponseType<ApiResponse<TargetAllocationResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<TargetAllocationResultDto>>> Allocate(
        Guid id,
        [FromBody] AllocateTargetDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.AllocateAsync(User.GetRequiredUserId(), id, dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, CreatedEnvelope(result, "Phân bổ target thành công."));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<TargetAssignmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TargetAssignmentDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    // ApiResponse.Ok defaults the envelope status to 200; keep it in step with the 201 HTTP status.
    private ApiResponse<T> CreatedEnvelope<T>(T data, string message) => new()
    {
        Success = true,
        Status = StatusCodes.Status201Created,
        Data = data,
        Message = message,
        TraceId = HttpContext.TraceIdentifier,
    };
}
