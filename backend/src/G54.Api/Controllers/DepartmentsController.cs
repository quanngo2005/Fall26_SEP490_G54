using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Department;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>Department view, tree and management. Contract: docs/api/DEPARTMENT_CONTRACT.md.</summary>
[ApiController]
[Route("departments")]
[HasPermission(OrganizationAccessCodes.Resource, OrganizationAccessCodes.Read, "Dept")]
public sealed class DepartmentsController(IDepartmentService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<DepartmentSummaryDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<DepartmentSummaryDto>>>> GetDepartments(
        [FromQuery] DepartmentFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDepartmentsAsync(User.GetRequiredUserId(), filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("tree")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DepartmentTreeNodeDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DepartmentTreeNodeDto>>>> GetTree(
        CancellationToken cancellationToken)
    {
        var result = await service.GetTreeAsync(User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [HasPermission("department", "manage", "All")]
    [ProducesResponseType<ApiResponse<DepartmentDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDetailDto>>> CreateDepartment(
        [FromBody] CreateDepartmentDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateDepartmentAsync(dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Tạo phòng ban thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetDepartment), new { id = result.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("department", "manage", "All")]
    [ProducesResponseType<ApiResponse<DepartmentDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDetailDto>>> UpdateDepartment(
        Guid id,
        [FromBody] UpdateDepartmentDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateDepartmentAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật phòng ban thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission("department", "manage", "All")]
    [ProducesResponseType<ApiResponse<DepartmentDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDetailDto>>> ChangeStatus(
        Guid id,
        [FromBody] DepartmentStatusDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.ChangeStatusAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật trạng thái phòng ban thành công.", HttpContext.TraceIdentifier));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission("department", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> DeleteDepartment(
        Guid id,
        CancellationToken cancellationToken)
    {
        await service.DeleteDepartmentAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok("Xóa phòng ban thành công.", HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<DepartmentDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DepartmentDetailDto>>> GetDepartment(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDepartmentAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}/overview")]
    [ProducesResponseType<ApiResponse<DepartmentOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DepartmentOverviewDto>>> GetOverview(
        Guid id,
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetOverviewAsync(User.GetRequiredUserId(), id, periodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}/members")]
    [ProducesResponseType<ApiResponse<PagedResult<DepartmentMemberDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<DepartmentMemberDto>>>> GetMembers(
        Guid id,
        [FromQuery] DepartmentMemberFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetMembersAsync(User.GetRequiredUserId(), id, filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
