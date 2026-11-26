using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.ServiceRatingImport;
using G54.BLL.Dtos.TransactionImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace G54.Api.Controllers;

/// <summary>Import of customer service ratings (UC29, UC32). Contract: docs/api/SERVICE_RATING_IMPORT_CONTRACT.md.</summary>
[ApiController]
[Route("service-rating-imports")]
[Authorize]
[HasPermission("service_rating_import", "manage", PermissionScopes.All)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class ServiceRatingImportsController(
    IServiceRatingImportService service,
    IOptions<SystemParametersOptions> parameters,
    IServiceRatingImportTemplateProvider templateProvider) : ControllerBase
{
    private const string XlsxExtension = ".xlsx";

    [HttpGet("template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTemplate() =>
        File(templateProvider.GetTemplate(), EvidenceFileValidator.ContentTypeFor(XlsxExtension), "service-rating-import-template.xlsx");

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<ApiResponse<ImportBatchDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportBatchDto>>> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        var maxMb = parameters.Value.ImportMaxFileMb;
        if (file is null || file.Length == 0 || file.Length > maxMb * 1024 * 1024 ||
            !file.FileName.EndsWith(XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ApiResponse.Fail(
                ErrorCodes.ImportInvalidFile,
                $"File must be a non-empty .xlsx of at most {maxMb} MB.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status400BadRequest));
        }

        await using var stream = file.OpenReadStream();
        var result = await service.ImportAsync(file.FileName, stream, User.GetRequiredUserId(), cancellationToken);
        // ApiResponse.Ok fixes the envelope status at 200; keep it in step with the 201 HTTP status.
        return Created($"/service-rating-imports/{result.Id}", new ApiResponse<ImportBatchDto>
        {
            Success = true,
            Status = StatusCodes.Status201Created,
            Data = result,
            Message = SuccessMessages.Created,
            TraceId = HttpContext.TraceIdentifier,
        });
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<ImportBatchDto>>>(StatusCodes.Status200OK)]
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

    [HttpGet("{id:guid}/ratings")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<ServiceRatingDto>>>> ListRatings(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await service.ListRatingsAsync(id, page, pageSize, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
