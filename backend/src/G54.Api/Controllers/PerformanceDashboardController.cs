using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.PerformanceDashboard;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Read-only performance dashboard for an employee, a department and a branch.
/// Contract: docs/api/PERFORMANCE_DASHBOARD_CONTRACT.md.
/// Errors are raised as AppException by the service and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Route("performance-dashboard")]
[HasPermission("evaluation", "read", "Own")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class PerformanceDashboardController(IPerformanceDashboardService service) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<PersonalPerformanceDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PersonalPerformanceDashboardDto>>> GetMine(
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetMyDashboardAsync(User.GetRequiredUserId(), periodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("employees/{employeeId:guid}")]
    [ProducesResponseType<ApiResponse<PersonalPerformanceDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PersonalPerformanceDashboardDto>>> GetEmployee(
        Guid employeeId,
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetEmployeeDashboardAsync(User.GetRequiredUserId(), employeeId, periodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("departments/{departmentId:guid}")]
    [HasPermission("evaluation", "read", "Dept")]
    [ProducesResponseType<ApiResponse<UnitPerformanceDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UnitPerformanceDashboardDto>>> GetDepartment(
        Guid departmentId,
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDepartmentDashboardAsync(User.GetRequiredUserId(), departmentId, periodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("branches/{branchId:guid}")]
    [HasPermission("evaluation", "read", "Dept")]
    [ProducesResponseType<ApiResponse<UnitPerformanceDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UnitPerformanceDashboardDto>>> GetBranch(
        Guid branchId,
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetBranchDashboardAsync(User.GetRequiredUserId(), branchId, periodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
