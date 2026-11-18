using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.WorkTransaction;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>Transaction codes of a work, entered by its assignee. Contract: docs/api/WORK_TRANSACTION_CONTRACT.md.</summary>
[ApiController]
[Authorize]
[Route("works/{workId:guid}/transactions")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
public sealed class WorkTransactionsController(IWorkTransactionService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<WorkTransactionsDto>>> List(Guid workId, CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(workId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<WorkTransactionsDto>>> Add(
        Guid workId, [FromBody] AddWorkTransactionsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await service.AddAsync(workId, User.GetRequiredUserId(), request, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("{transactionId:guid}")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<WorkTransactionsDto>>> Remove(
        Guid workId, Guid transactionId, CancellationToken cancellationToken)
    {
        var result = await service.RemoveAsync(workId, transactionId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
