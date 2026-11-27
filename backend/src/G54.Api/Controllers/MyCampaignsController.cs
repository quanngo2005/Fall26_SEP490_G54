using G54.Api.Authorization;
using G54.BLL.Common;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.CampaignCustomer;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Employee-side "My campaigns": the campaigns I have assigned customers in, my customers in a campaign, and the
/// contact-update action (docs/api/CAMPAIGN_CONTRACT.md §5). Business rules, ownership checks and the Work created
/// on close all live in <see cref="IMyCampaignService"/>; this controller only forwards requests.
/// </summary>
[ApiController]
[Route("my-campaigns")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class MyCampaignsController(IMyCampaignService myCampaignService) : ControllerBase
{
    [HttpGet]
    [HasPermission("campaign_customer", "update", "Own")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MyCampaignSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyCampaigns(CancellationToken cancellationToken)
    {
        var result = await myCampaignService.GetMyCampaignsAsync(User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Lấy danh sách chiến dịch của tôi thành công.", HttpContext.TraceIdentifier));
    }

    [HttpGet("{campaignId:guid}/customers")]
    [HasPermission("campaign_customer", "update", "Own")]
    [ProducesResponseType<ApiResponse<PagedResult<CampaignCustomerDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyCustomers(
        Guid campaignId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await myCampaignService.GetMyCustomersAsync(
            campaignId, User.GetRequiredUserId(), search, status, page, pageSize, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Lấy danh sách khách hàng của tôi thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPatch("{campaignId:guid}/customers/{customerId:guid}/contact")]
    [HasPermission("campaign_customer", "update", "Own")]
    [ProducesResponseType<ApiResponse<CampaignCustomerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateContact(
        Guid campaignId,
        Guid customerId,
        [FromBody] UpdateCampaignCustomerContactRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await myCampaignService.UpdateContactAsync(
            campaignId, customerId, User.GetRequiredUserId(), request, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật trạng thái liên hệ thành công.", HttpContext.TraceIdentifier));
    }
}
