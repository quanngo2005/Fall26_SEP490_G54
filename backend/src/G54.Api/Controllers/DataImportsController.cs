using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.DataImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace G54.Api.Controllers;

/// <summary>
/// Generic 2-step (preview -&gt; commit) data import API, Admin-only. Contract: docs/api/DATA_IMPORT_CONTRACT.md.
/// Business rules live in the resolved <c>IImportDefinition</c> (e.g. Employee/Organization); this controller
/// only validates the upload shape and forwards to <see cref="IDataImportService"/>.
/// </summary>
[ApiController]
[Route("data-imports")]
[Authorize]
[HasPermission("data_import", "import", PermissionScopes.All)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class DataImportsController(IDataImportService service, IOptions<SystemParametersOptions> parameters) : ControllerBase
{
    private const string XlsxExtension = ".xlsx";

    [HttpGet("{type}/template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetTemplate(string type)
    {
        var bytes = service.GetTemplate(type);
        return File(bytes, EvidenceFileValidator.ContentTypeFor(XlsxExtension), $"{type}-template.xlsx");
    }

    [HttpPost("{type}/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB hard cap; SystemParameters:ImportMaxFileMb is the configurable limit
    [ProducesResponseType<ApiResponse<ImportPreviewDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ImportPreviewDto>>> Preview(
        string type, IFormFile? file, CancellationToken cancellationToken)
    {
        var maxMb = parameters.Value.ImportMaxFileMb;
        var maxBytes = maxMb * 1024 * 1024;

        if (file is null || file.Length == 0 || file.Length > maxBytes || !await IsValidXlsxAsync(file))
        {
            return BadRequest(ApiResponse.Fail(
                ErrorCodes.ValidationError,
                $"File phải là .xlsx hợp lệ, không rỗng, tối đa {maxMb} MB.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status400BadRequest));
        }

        await using var stream = file.OpenReadStream();
        var result = await service.PreviewAsync(type, file.FileName, stream, User.GetRequiredUserId(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse.Ok(result, "Preview import succeeded.", HttpContext.TraceIdentifier));
    }

    [HttpGet("{importId:guid}")]
    [ProducesResponseType<ApiResponse<ImportPreviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ImportPreviewDto>>> GetPreview(
        Guid importId, [FromQuery] ImportPreviewQueryDto query, CancellationToken cancellationToken)
    {
        var result = await service.GetPreviewAsync(importId, query, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{importId:guid}/commit")]
    [ProducesResponseType<ApiResponse<ImportCommitResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<ImportCommitResultDto>>> Commit(Guid importId, CancellationToken cancellationToken)
    {
        var result = await service.CommitAsync(importId, User.GetRequiredUserId(), cancellationToken);
        // Temporary passwords are returned exactly once; never cached, never logged.
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse.Ok(result, "Commit import succeeded.", HttpContext.TraceIdentifier));
    }

    [HttpDelete("{importId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid importId, CancellationToken cancellationToken)
    {
        await service.CancelAsync(importId, User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    private static async Task<bool> IsValidXlsxAsync(IFormFile file)
    {
        if (!string.Equals(Path.GetExtension(file.FileName), XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var header = new byte[EvidenceFileValidator.HeaderLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header);
        return EvidenceFileValidator.HasValidSignature(XlsxExtension, header.AsSpan(0, read));
    }
}
