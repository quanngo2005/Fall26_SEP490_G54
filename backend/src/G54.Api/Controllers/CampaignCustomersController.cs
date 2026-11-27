using G54.Api.Authorization;
using G54.Api.Common;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.CampaignCustomer;
using G54.BLL.Dtos.DataImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace G54.Api.Controllers;

/// <summary>
/// Campaign customers: Excel import and the manager-side list/allocate/reassign actions
/// (docs/api/CAMPAIGN_CONTRACT.md §5). Import is stateless: preview validates the uploaded file, commit takes the
/// same file again. Business rules live in <see cref="ICampaignCustomerImportService"/> and
/// <see cref="ICampaignCustomerService"/>; this controller only validates the upload shape and forwards requests.
/// </summary>
[ApiController]
[Route("campaigns/{campaignId:guid}/customers")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class CampaignCustomersController(
    ICampaignCustomerImportService importService,
    ICampaignCustomerService customerService,
    IOptions<SystemParametersOptions> parameters) : ControllerBase
{
    private const string XlsxExtension = ".xlsx";

    [HttpGet]
    [HasPermission("campaign", "read", "Dept")]
    [ProducesResponseType<ApiResponse<CampaignCustomerListResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomers(
        Guid campaignId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool unassignedOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await customerService.ListAsync(
            campaignId, search, status, assigneeId, unassignedOnly, page, pageSize, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Lấy danh sách khách hàng chiến dịch thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPost("allocate")]
    [HasPermission("campaign", "manage", "Dept")]
    [ProducesResponseType<ApiResponse<AllocateCampaignCustomersResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Allocate(
        Guid campaignId, [FromBody] AllocateCampaignCustomersRequestDto request, CancellationToken cancellationToken)
    {
        var result = await customerService.AllocateAsync(
            campaignId, request.Rule, request.EmployeeIds, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Phân bổ khách hàng thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPut("{customerId:guid}/assignee")]
    [HasPermission("campaign", "manage", "Dept")]
    [ProducesResponseType<ApiResponse<CampaignCustomerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reassign(
        Guid campaignId,
        Guid customerId,
        [FromBody] ReassignCampaignCustomerRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await customerService.ReassignAsync(
            campaignId, customerId, request.EmployeeId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật người phụ trách thành công.", HttpContext.TraceIdentifier));
    }

    [HttpGet("import/template")]
    [HasPermission("campaign", "read", "Dept")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTemplate() =>
        File(importService.GetTemplate(), EvidenceFileValidator.ContentTypeFor(XlsxExtension), "campaign-customers-template.xlsx");

    [HttpPost("import/preview")]
    [HasPermission("campaign", "manage", "Dept")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB hard cap; SystemParameters:ImportMaxFileMb is the configurable limit
    [ProducesResponseType<ApiResponse<ImportPreviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Preview(Guid campaignId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (!await XlsxUploadValidator.IsAcceptableAsync(file, parameters.Value.ImportMaxFileMb))
        {
            return InvalidFile();
        }

        await using var stream = file!.OpenReadStream();
        var result = await importService.PreviewAsync(campaignId, file.FileName, stream, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Preview import succeeded.", HttpContext.TraceIdentifier));
    }

    [HttpPost("import/commit")]
    [HasPermission("campaign", "manage", "Dept")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<ApiResponse<ImportCommitResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Commit(Guid campaignId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (!await XlsxUploadValidator.IsAcceptableAsync(file, parameters.Value.ImportMaxFileMb))
        {
            return InvalidFile();
        }

        await using var stream = file!.OpenReadStream();
        var result = await importService.CommitAsync(campaignId, stream, User.GetRequiredUserId(), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse.Ok(result, "Commit import succeeded.", HttpContext.TraceIdentifier));
    }

    private BadRequestObjectResult InvalidFile() => BadRequest(ApiResponse.Fail(
        ErrorCodes.ValidationError,
        $"File phải là .xlsx hợp lệ, không rỗng, tối đa {parameters.Value.ImportMaxFileMb} MB.",
        traceId: HttpContext.TraceIdentifier,
        status: StatusCodes.Status400BadRequest));
}
