using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.SystemParameters;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("system-parameters")]
[Authorize]
public sealed class SystemParametersController(ISystemParameterService service) : ControllerBase
{
    [HasPermission("system-parameter", "read", "All")]
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SystemParameterDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SystemParameterDto>>>> GetAll(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.GetAllAsync(User.GetRequiredUserId(), ct), traceId: HttpContext.TraceIdentifier));

    [HasPermission("system-parameter", "manage", "All")]
    [HttpPut]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SystemParameterDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SystemParameterDto>>>> Save(SaveSystemParametersDto request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.SaveAsync(User.GetRequiredUserId(), request, ct), SuccessMessages.Updated, traceId: HttpContext.TraceIdentifier));
}
