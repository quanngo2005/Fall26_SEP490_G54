using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Reconciliation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Reconciliation of every transaction code against the imported Core Banking file (SCR-REV-03 / SCR-REV-03-D1).
/// Contract: docs/api/RECONCILIATION_CONTRACT.md. The scope (QA and administrators: all, managers: their department or
/// branch) is applied by the service from the caller's <c>reconciliation:read</c> scope.
/// </summary>
[ApiController]
[Authorize]
[Route("reconciliations")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class ReconciliationsController(IReconciliationService service) : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    [HasPermission("reconciliation", "read", "Dept")]
    [ProducesResponseType<ApiResponse<ReconciliationListDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ReconciliationListDto>>> List(
        [FromQuery] ReconciliationFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(User.GetRequiredUserId(), filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [HasPermission("reconciliation", "read", "Dept")]
    [ProducesResponseType<ApiResponse<ReconciliationDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ReconciliationDetailDto>>> GetDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDetailAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>The list as an Excel workbook (the filters apply, every page).</summary>
    [HttpGet("export")]
    [HasPermission("reconciliation", "read", "Dept")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ExcelContentType)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        [FromQuery] ReconciliationFilterDto filter,
        CancellationToken cancellationToken)
    {
        var file = await service.ExportAsync(User.GetRequiredUserId(), filter, cancellationToken);
        return File(file.Content, ExcelContentType, file.FileName);
    }
}
