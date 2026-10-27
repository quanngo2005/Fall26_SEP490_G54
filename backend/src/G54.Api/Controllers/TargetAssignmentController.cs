using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("target-assignments")]
[HasPermission("target", "manage", "Department")]
public sealed class TargetAssignmentController(ITargetAllocationService service, ISystemParameterReader parameterReader) : ControllerBase
{

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB (max of importMaxFileMb parameter)
    [ProducesResponseType<ApiResponse<TargetImportResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<TargetImportResultDto>>> Import(
        [FromQuery] Guid branchId,
        [FromQuery] int year,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var maxMb = await parameterReader.GetLongAsync(SystemParameterKeys.ImportMaxFileMb, cancellationToken);
        var maxBytes = maxMb * 1024 * 1024;

        if (file is null || file.Length == 0 || file.Length > maxBytes ||
            !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ApiResponse.Fail(
                ErrorCodes.ValidationError,
                $"File must be a non-empty .xlsx of at most {maxMb} MB.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status400BadRequest));
        }

        await using var stream = file.OpenReadStream();
        var result = await service.ImportYearTargetsAsync(branchId, year, stream, User.GetRequiredUserId(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse.Ok(result, "Import year targets succeeded.", HttpContext.TraceIdentifier));
    }

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
        var result = await service.GetTreeAsync(branchId, year, compareYear, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
