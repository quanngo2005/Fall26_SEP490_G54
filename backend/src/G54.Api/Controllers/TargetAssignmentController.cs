using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Dtos.TargetImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace G54.Api.Controllers;

[ApiController]
[Route("target-assignments")]
[HasPermission("target", "manage", PermissionScopes.Dept)]
public sealed class TargetAssignmentController(
    ITargetImportService importService,
    ITargetSetupService setupService,
    IOptions<SystemParametersOptions>? parameters = null) : ControllerBase
{
    private const int HardRequestLimitMb = 50;
    private const string XlsxExtension = ".xlsx";
    private int MaxFileMb => Math.Max(1, parameters?.Value.TargetImportMaxFileMb ?? 10);

    [HttpGet("import/template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetImportTemplate([FromQuery] int year)
    {
        var bytes = importService.BuildTemplate(year);
        return File(bytes, EvidenceFileValidator.ContentTypeFor(XlsxExtension), $"KPI-Nam-Quy-{year}-mau.xlsx");
    }

    [HttpPost("import/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(HardRequestLimitMb * 1024 * 1024)]
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
    [RequestSizeLimit(HardRequestLimitMb * 1024 * 1024)]
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

    [HttpGet("{branchYearId:guid}/allocations")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<KpiAllocationDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<KpiAllocationDto>>>> GetAllAllocations(
        Guid branchYearId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.GetAllAllocationsAsync(branchYearId, User.GetRequiredUserId(), cancellationToken);
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
    // Phase 3: Tier 3 employee allocation and publish (docs/api/TARGET_ALLOCATION_CONTRACT.md, Phase 3)
    // deptYearId already pins department + KPI + year (one dept-year row per KPI per department), so there is
    // no separate kpiId route segment here (unlike the branch-year anchor, which covers several KPI rows).
    // =========================================================================

    [HttpGet("{deptYearId:guid}/employee-allocation")]
    [ProducesResponseType<ApiResponse<EmployeeAllocationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeAllocationDto>>> GetEmployeeAllocation(
        Guid deptYearId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.GetEmployeeAllocationAsync(deptYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{deptYearId:guid}/employee-allocations")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<EmployeeAllocationDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeAllocationDto>>>> GetAllEmployeeAllocations(
        Guid deptYearId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.GetAllEmployeeAllocationsAsync(deptYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("{deptYearId:guid}/employee-allocation")]
    [ProducesResponseType<ApiResponse<EmployeeAllocationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<EmployeeAllocationDto>>> SaveEmployeeAllocation(
        Guid deptYearId,
        [FromBody] SaveEmployeeAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setupService.SaveEmployeeAllocationAsync(deptYearId, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{deptYearId:guid}/publish-employees")]
    [ProducesResponseType<ApiResponse<PublishEmployeesResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<PublishEmployeesResultDto>>> PublishEmployees(
        Guid deptYearId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.PublishEmployeesAsync(deptYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Published successfully.", HttpContext.TraceIdentifier));
    }

    // =========================================================================
    // Mid-term adjustment (Target Allocation v3, D12/D13): the only way to change a published target.
    // =========================================================================

    [HttpPut("{branchYearId:guid}/quarter-split/adjust")]
    [ProducesResponseType<ApiResponse<QuarterSplitDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<QuarterSplitDto>>> AdjustQuarters(
        Guid branchYearId,
        [FromBody] AdjustQuarterSplitRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setupService.AdjustQuartersAsync(branchYearId, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Đã điều chỉnh chỉ tiêu theo quý.", HttpContext.TraceIdentifier));
    }

    [HttpPut("{branchYearId:guid}/kpis/{kpiId:guid}/allocation/adjust")]
    [ProducesResponseType<ApiResponse<KpiAllocationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<KpiAllocationDto>>> AdjustAllocation(
        Guid branchYearId,
        Guid kpiId,
        [FromBody] AdjustKpiAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setupService.AdjustAllocationAsync(branchYearId, kpiId, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Đã điều chỉnh chỉ tiêu phòng ban.", HttpContext.TraceIdentifier));
    }

    [HttpPut("{deptYearId:guid}/employee-allocation/adjust")]
    [ProducesResponseType<ApiResponse<EmployeeAllocationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<EmployeeAllocationDto>>> AdjustEmployeeAllocation(
        Guid deptYearId,
        [FromBody] AdjustEmployeeAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setupService.AdjustEmployeeAllocationAsync(deptYearId, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Đã điều chỉnh chỉ tiêu nhân viên.", HttpContext.TraceIdentifier));
    }

    [HttpPost("{branchYearId:guid}/departments/{departmentId:guid}/remind")]
    [ProducesResponseType<ApiResponse<RemindDepartmentManagerResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RemindDepartmentManagerResultDto>>> RemindDepartmentManager(
        Guid branchYearId,
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.RemindDepartmentManagerAsync(branchYearId, departmentId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Đã gửi nhắc nhở.", HttpContext.TraceIdentifier));
    }

    [HttpGet("{branchYearId:guid}/progress")]
    [ProducesResponseType<ApiResponse<ProgressRollupDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProgressRollupDto>>> GetProgressRollup(
        Guid branchYearId,
        CancellationToken cancellationToken)
    {
        var result = await setupService.GetProgressRollupAsync(branchYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
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
        var result = await setupService.AllocateDepartmentsAsync(branchYearId, dto, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("{deptYearId:guid}/quarters")]
    public async Task<ActionResult<ApiResponse<List<TargetNodeDto>>>> AllocateQuarters(
        Guid deptYearId,
        [FromBody] AllocateQuartersDto dto,
        CancellationToken cancellationToken)
    {
        var result = await setupService.AllocateQuartersAsync(deptYearId, dto, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("{deptYearId:guid}/confirm")]
    public async Task<ActionResult<ApiResponse<TargetNodeDto>>> Confirm(Guid deptYearId, CancellationToken cancellationToken)
    {
        var result = await setupService.ConfirmAsync(deptYearId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<List<TargetNodeDto>>>> GetTree(
        [FromQuery] Guid branchId,
        [FromQuery] int year,
        [FromQuery] int? compareYear,
        CancellationToken cancellationToken)
    {
        var result = await setupService.GetTreeAsync(branchId, year, compareYear, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    private async Task<bool> IsAcceptedFileAsync(IFormFile? file) =>
        file is not null && file.Length > 0 && file.Length <= (long)MaxFileMb * 1024 * 1024 && await IsValidXlsxAsync(file);

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
