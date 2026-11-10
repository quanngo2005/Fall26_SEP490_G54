using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Dtos.TargetImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("target-assignments")]
[HasPermission("target", "manage", "Department")]
public sealed class TargetAssignmentController(
    ITargetAllocationService service,
    ITargetImportService importService,
    ITargetSetupService setupService) : ControllerBase
{
    // G4: 10 MB for the year-target import only; the generic data-import default (SystemParameters:ImportMaxFileMb) is unaffected.
    private const int MaxFileMb = 10;
    private const string XlsxExtension = ".xlsx";

    [HttpGet("import/template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetImportTemplate([FromQuery] int year)
    {
        var bytes = importService.BuildTemplate(year);
        return File(bytes, EvidenceFileValidator.ContentTypeFor(XlsxExtension), $"KPI-Nam-Quy-{year}-mau.xlsx");
    }

    [HttpPost("import/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxFileMb * 1024 * 1024)]
    [ProducesResponseType<ApiResponse<TargetImportPreviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<TargetImportPreviewDto>>> PreviewImport(
        [FromQuery] Guid branchId,
        [FromQuery] int year,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!await IsAcceptedFileAsync(file))
        {
            return InvalidFile();
        }

        await using var stream = file!.OpenReadStream();
        var result = await importService.PreviewAsync(branchId, year, file.FileName, stream, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("import/commit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxFileMb * 1024 * 1024)]
    [ProducesResponseType<ApiResponse<TargetImportCommitResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<TargetImportCommitResultDto>>> CommitImport(
        [FromQuery] Guid branchId,
        [FromQuery] int year,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!await IsAcceptedFileAsync(file))
        {
            return InvalidFile();
        }

        await using var stream = file!.OpenReadStream();
        var result = await importService.CommitAsync(branchId, year, file.FileName, stream, User.GetRequiredUserId(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse.Ok(result, "Import year targets succeeded.", HttpContext.TraceIdentifier));
    }

    [HttpGet("setup")]
    [ProducesResponseType<ApiResponse<TargetSetupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TargetSetupDto>>> GetSetup(
        [FromQuery] Guid branchId, [FromQuery] int year, CancellationToken cancellationToken)
    {
        // Phase 2: Use TargetSetupService for full state (Phase 1 returns "IMPORT" step if no branch-year)
        var result = await setupService.GetSetupAsync(branchId, year, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    // =========================================================================
    // Phase 2: Quarter split, allocation, and publish (docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A)
    // =========================================================================

    [HttpGet("{branchYearId:guid}/quarter-split")]
    [ProducesResponseType<ApiResponse<QuarterSplitDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<QuarterSplitDto>>> GetQuarters(
        Guid branchYearId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.GetQuartersAsync(branchYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("{branchYearId:guid}/quarter-split")]
    [ProducesResponseType<ApiResponse<QuarterSplitDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<QuarterSplitDto>>> SaveQuarters(
        Guid branchYearId,
        [FromBody] SaveQuarterSplitRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setupService.SaveQuartersAsync(branchYearId, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{branchYearId:guid}/kpis/{kpiId:guid}/allocation")]
    [ProducesResponseType<ApiResponse<KpiAllocationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<KpiAllocationDto>>> GetAllocation(
        Guid branchYearId,
        Guid kpiId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.GetAllocationAsync(branchYearId, kpiId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("{branchYearId:guid}/kpis/{kpiId:guid}/allocation")]
    [ProducesResponseType<ApiResponse<KpiAllocationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<KpiAllocationDto>>> SaveAllocation(
        Guid branchYearId,
        Guid kpiId,
        [FromBody] SaveKpiAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setupService.SaveAllocationAsync(branchYearId, kpiId, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{branchYearId:guid}/publish")]
    [ProducesResponseType<ApiResponse<PublishResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<PublishResultDto>>> Publish(
        Guid branchYearId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.PublishAsync(branchYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Published successfully.", HttpContext.TraceIdentifier));
    }

    // =========================================================================
    // Deprecated endpoints (Phase 2.1+) - marked for future removal
    // These are kept for backward compatibility but replaced by Phase 2 endpoints above
    // =========================================================================

    [HttpPut("{branchYearId:guid}/departments")]
    public async Task<ActionResult<ApiResponse<List<TargetNodeDto>>>> AllocateDepartments(
        Guid branchYearId,
        [FromBody] AllocateDepartmentsDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.AllocateDepartmentsAsync(branchYearId, dto, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("{deptYearId:guid}/quarters")]
    public async Task<ActionResult<ApiResponse<List<TargetNodeDto>>>> AllocateQuarters(
        Guid deptYearId,
        [FromBody] AllocateQuartersDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.AllocateQuartersAsync(deptYearId, dto, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{deptYearId:guid}/confirm")]
    public async Task<ActionResult<ApiResponse<TargetNodeDto>>> Confirm(Guid deptYearId, CancellationToken cancellationToken)
    {
        var result = await service.ConfirmAsync(deptYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<List<TargetNodeDto>>>> GetTree(
        [FromQuery] Guid branchId,
        [FromQuery] int year,
        [FromQuery] int? compareYear,
        CancellationToken cancellationToken)
    {
        var result = await service.GetTreeAsync(branchId, year, compareYear, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    private static async Task<bool> IsAcceptedFileAsync(IFormFile? file) =>
        file is not null && file.Length > 0 && file.Length <= MaxFileMb * 1024 * 1024 && await IsValidXlsxAsync(file);

    private BadRequestObjectResult InvalidFile() => BadRequest(ApiResponse.Fail(
        ErrorCodes.ValidationError,
        $"File phải là .xlsx hợp lệ, không rỗng, tối đa {MaxFileMb} MB.",
        traceId: HttpContext.TraceIdentifier,
        status: StatusCodes.Status400BadRequest));

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
