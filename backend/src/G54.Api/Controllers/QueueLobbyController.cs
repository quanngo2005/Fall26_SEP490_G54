using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Queue;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Authorize]
[HasPermission("queue_counter", "supervise", "Dept")]
[Route("queue-counters/lobby")]
public sealed class QueueLobbyController(QueueLobbyService lobby) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<LobbyState>>> Get(CancellationToken ct) =>
        Ok(ApiResponse.Ok(await lobby.ReadAsync(User.GetRequiredUserId(), null, null, ct)));

    [HttpPut("desks/{counter:int}")]
    public async Task<ActionResult<ApiResponse<LobbyState>>> Update(int counter, LobbyUpdate input, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await lobby.ReadAsync(User.GetRequiredUserId(), counter, input, ct)));
}
