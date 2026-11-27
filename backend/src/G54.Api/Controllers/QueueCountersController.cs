using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Queue;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Authorize]
[Route("queue-counters")]
public sealed class QueueCountersController(QueueCounterService service) : ControllerBase
{
    [HttpGet]
    [HasPermission("queue_counter", "operate", "Own")]
    public async Task<ActionResult<ApiResponse<CounterState>>> Get(CancellationToken ct) =>
        Ok(ApiResponse.Ok(await service.GetAsync(User.GetRequiredUserId(), null, ct)));

    [HttpPost("commands")]
    [HasPermission("queue_counter", "operate", "Own")]
    public async Task<ActionResult<ApiResponse<CounterState>>> Command(CounterCommand command, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await service.GetAsync(User.GetRequiredUserId(), command, ct)));

    [HttpGet("ratings.xlsx")]
    [HasPermission("service_rating_import", "manage", "All")]
    public async Task<IActionResult> Ratings(CancellationToken ct) =>
        File(await service.ExportAsync(ct), EvidenceFileValidator.ContentTypeFor(".xlsx"), "queue-ratings.xlsx");
}
