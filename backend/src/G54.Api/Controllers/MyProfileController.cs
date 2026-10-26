using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Auth;
using G54.BLL.Dtos.Profile;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// My Profile API (change phone, change password). Contract: docs/api/MY_PROFILE_CONTRACT.md. Shares the <c>me</c> root
/// with <see cref="MeController"/>; the profile itself is read from <c>GET /employees/me</c>.
/// Errors are raised as AppException by the services and mapped by GlobalExceptionHandler.
/// </summary>
[ApiController]
[Authorize]
[Route("me")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class MyProfileController(IProfileService profileService, IAuthService authService) : ControllerBase
{
    [HttpPut("profile")]
    [ProducesResponseType<ApiResponse<MyPhoneDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MyPhoneDto>>> UpdateProfile(
        [FromBody] UpdateMyProfileDto request,
        CancellationToken cancellationToken)
    {
        var result = await profileService.UpdatePhoneAsync(User.GetRequiredUserId(), request, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Changes the password, revokes every other session and returns a fresh token pair for this one.</summary>
    [HttpPost("password")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponseDto>> ChangePassword(
        [FromBody] ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var result = await authService.ChangePasswordAsync(
            User.GetRequiredUserId(),
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return Ok(result);
    }
}
