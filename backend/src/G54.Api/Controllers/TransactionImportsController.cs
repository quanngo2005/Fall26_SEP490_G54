using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TransactionImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace G54.Api.Controllers;

/// <summary>Admin import of Core Banking transactions. Contract: docs/api/TRANSACTION_IMPORT_CONTRACT.md.</summary>
[ApiController]
[Route("transaction-imports")]
[Authorize]
[HasPermission("transaction-import", "manage", PermissionScopes.All)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class TransactionImportsController(
    ITransactionImportService service,
    IWorkTransactionService workTransactions,
    IOptions<SystemParametersOptions> parameters,
    ITransactionImportTemplateProvider templateProvider) : ControllerBase
{
    private const string XlsxExtension = ".xlsx";

    [HttpGet("template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTemplate() =>
        File(templateProvider.GetTemplate(), EvidenceFileValidator.ContentTypeFor(XlsxExtension), "transaction-import-template.xlsx");

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<ApiResponse<ImportBatchDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportBatchDto>>> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        var maxMb = parameters.Value.ImportMaxFileMb;
        if (file is null || file.Length == 0 || file.Length > maxMb * 1024 * 1024 ||
            !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ApiResponse.Fail(
                ErrorCodes.ImportInvalidFile,
                $"File must be a non-empty .xlsx of at most {maxMb} MB.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status400BadRequest));
        }

        await using var stream = file.OpenReadStream();
        var result = await service.ImportAsync(file.FileName, stream, User.GetRequiredUserId(), cancellationToken);
        result = result with { Reconcile = await workTransactions.ReconcileBatchAsync(result.Id, cancellationToken) };
        return Created($"/transaction-imports/{result.Id}", ApiResponse.Ok(result, SuccessMessages.Created, HttpContext.TraceIdentifier));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ImportBatchDto>>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await service.ListBatchesAsync(page, pageSize, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ImportBatchDto>>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetBatchAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}/errors")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<ImportRowErrorDto>>>> ListErrors(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await service.ListErrorsAsync(id, page, pageSize, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}/transactions")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<ImportedTransactionDto>>>> ListTransactions(
        Guid id, [FromQuery] string? employeeCode, [FromQuery] string? productCode,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await service.ListTransactionsAsync(id, employeeCode, productCode, page, pageSize, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
