using System.Security.Claims;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(IPermissionService permissionService) : ControllerBase
{
    [HttpGet("permissions")]
    [ProducesResponseType<ApiResponse<UserPermissionsResponseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserPermissionsResponseDto>>> GetPermissions(CancellationToken cancellationToken)
    {
        var sub = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(sub, out var accountId))
        {
            return Unauthorized(ApiResponse.Fail(
                ErrorCodes.Unauthorized,
                "Không xác định được danh tính người dùng.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status401Unauthorized));
        }

        var result = await permissionService.GetUserPermissionsAsync(accountId, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy thông tin tài khoản hoặc quyền hạn.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
