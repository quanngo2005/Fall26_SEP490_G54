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
/// Employee/organization Excel import, Admin-only. Contract: docs/api/DATA_IMPORT_CONTRACT.md. Stateless:
/// preview validates the uploaded file, commit takes the same file again. Business rules live in
/// <see cref="IEmployeeImportService"/>; this controller only validates the upload shape.
/// </summary>
[ApiController]
[Route("data-imports")]
[Authorize]
[HasPermission("data_import", "import", PermissionScopes.All)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class DataImportsController(IEmployeeImportService service, IOptions<SystemParametersOptions> parameters) : ControllerBase
{
    private const string XlsxExtension = ".xlsx";

    [HttpGet("employees/template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTemplate() =>
        File(service.GetTemplate(), EvidenceFileValidator.ContentTypeFor(XlsxExtension), "employees-template.xlsx");

    [HttpPost("employees/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB hard cap; SystemParameters:ImportMaxFileMb is the configurable limit
    [ProducesResponseType<ApiResponse<ImportPreviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken cancellationToken)
    {
        if (!await IsAcceptableAsync(file))
        {
            return InvalidFile();
        }

        await using var stream = file!.OpenReadStream();
        var result = await service.PreviewAsync(file.FileName, stream, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Preview import succeeded.", HttpContext.TraceIdentifier));
    }

    [HttpPost("employees/commit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<ApiResponse<ImportCommitResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Commit(IFormFile? file, CancellationToken cancellationToken)
    {
        if (!await IsAcceptableAsync(file))
        {
            return InvalidFile();
        }

        await using var stream = file!.OpenReadStream();
        var result = await service.CommitAsync(stream, cancellationToken);
        // Temporary passwords are returned exactly once; never cached, never logged.
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse.Ok(result, "Commit import succeeded.", HttpContext.TraceIdentifier));
    }

    private BadRequestObjectResult InvalidFile() => BadRequest(ApiResponse.Fail(
        ErrorCodes.ValidationError,
        $"File phải là .xlsx hợp lệ, không rỗng, tối đa {parameters.Value.ImportMaxFileMb} MB.",
        traceId: HttpContext.TraceIdentifier,
        status: StatusCodes.Status400BadRequest));

    private async Task<bool> IsAcceptableAsync(IFormFile? file)
    {
        var maxBytes = parameters.Value.ImportMaxFileMb * 1024L * 1024;
        return file is not null && file.Length > 0 && file.Length <= maxBytes && await IsValidXlsxAsync(file);
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
