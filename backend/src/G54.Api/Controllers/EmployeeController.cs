using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Employee;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("employees")]
[Authorize]
public sealed class EmployeeController(IEmployeeService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<EmployeeDetailDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeDetailDto>>>> GetEmployees(
        [FromQuery] EmployeeFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetEmployeesAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>The signed-in user's own employee record; <c>/employee-detail/me</c> in the UI.</summary>
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<EmployeeDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> GetCurrentEmployee(
        CancellationToken cancellationToken)
    {
        var result = await service.GetCurrentEmployeeAsync(User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Employee codes are unique and already shown in the UI, so URLs use them instead of the GUID.</summary>
    [HttpGet("by-code/{code}")]
    [ProducesResponseType<ApiResponse<EmployeeDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> GetEmployeeByCode(
        string code,
        CancellationToken cancellationToken)
    {
        var result = await service.GetEmployeeByCodeAsync(code, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy nhân viên yêu cầu.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<EmployeeDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> GetEmployeeById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetEmployeeByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy nhân viên yêu cầu.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("departments")]
    [ProducesResponseType<ApiResponse<List<DepartmentOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<DepartmentOptionDto>>>> GetDepartmentOptions(
        CancellationToken cancellationToken)
    {
        var result = await service.GetDepartmentOptionsAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("job-positions")]
    [ProducesResponseType<ApiResponse<List<JobPositionOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<JobPositionOptionDto>>>> GetJobPositionOptions(
        CancellationToken cancellationToken)
    {
        var result = await service.GetJobPositionOptionsAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("managers")]
    [ProducesResponseType<ApiResponse<List<ManagerOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ManagerOptionDto>>>> GetManagerOptions(
        [FromQuery] Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetManagerOptionsAsync(excludeId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [HasPermission("employee", "manage", "All")]
    [ProducesResponseType<ApiResponse<EmployeeDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> CreateEmployee(
        [FromBody] CreateEmployeeDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateEmployeeAsync(dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Tạo nhân viên thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetEmployeeById), new { id = result.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("employee", "manage", "All")]
    [ProducesResponseType<ApiResponse<EmployeeDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDetailDto>>> UpdateEmployee(
        Guid id,
        [FromBody] UpdateEmployeeDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateEmployeeAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật thông tin nhân viên thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission("employee", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> ChangeEmployeeStatus(
        Guid id,
        [FromBody] EmployeeStatusDto dto,
        CancellationToken cancellationToken)
    {
        await service.ChangeStatusAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok("Cập nhật trạng thái nhân viên thành công.", HttpContext.TraceIdentifier));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission("employee", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> DeleteEmployee(
        Guid id,
        CancellationToken cancellationToken)
    {
        await service.DeleteEmployeeAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok("Xóa nhân viên thành công.", HttpContext.TraceIdentifier));
    }
}
