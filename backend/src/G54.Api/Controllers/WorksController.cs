using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("api/v1/works")]
[Authorize]
public sealed class WorksController(IWorkService workService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ApiResponse<WorkDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkDetailDto>>> Create(
        [FromBody] CreateWorkRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workService.CreateWorkAsync(request, cancellationToken);
        var response = ApiResponse.Ok(
            result,
            "Khởi tạo và phân công công việc thành công.",
            HttpContext.TraceIdentifier);

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<WorkDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkDetailDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await workService.GetByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                errorCode: ErrorCodes.NotFound,
                message: "Không tìm thấy công việc yêu cầu.",
                errors: null,
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{id:guid}/transitions")]
    [ProducesResponseType<ApiResponse<WorkDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<WorkDetailDto>>> Transition(
        Guid id,
        [FromBody] TransitionWorkRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workService.TransitionWorkAsync(id, request, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Chuyển trạng thái công việc thành công.", HttpContext.TraceIdentifier));
    }
}
