using System.Security.Claims;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Sidebar;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("sidebar-configurations")]
[Authorize]
public sealed class SidebarConfigurationsController(ISidebarConfigurationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SidebarConfigurationDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SidebarConfigurationDto>>>> GetAll(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await service.GetAsync(AccountId(), true, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpGet("current")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SidebarConfigurationDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SidebarConfigurationDto>>>> GetCurrent(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await service.GetAsync(AccountId(), false, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpPut("{role}")]
    [ProducesResponseType<ApiResponse<SidebarConfigurationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SidebarConfigurationDto>>> Save(string role, SaveSidebarConfigurationDto request, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await service.SaveAsync(AccountId(), role, request, cancellationToken), traceId: HttpContext.TraceIdentifier));

    private Guid AccountId()
    {
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out var id) ? id : throw new UnauthorizedException();
    }
}
