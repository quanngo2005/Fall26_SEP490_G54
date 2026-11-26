using G54.BLL.Common.Models;
using G54.BLL.Dtos.CampaignCustomer;

namespace G54.BLL.Services;

/// <summary>
/// Employee-side campaign customer operations ("My campaigns"): my campaigns, my customers in a campaign, and
/// the contact-update action that can close a customer into a <c>Work</c>. Contract: docs/api/CAMPAIGN_CONTRACT.md
/// §5. Contact status never writes KPI; closing only creates a Work that later feeds KPI through the existing
/// submission/verification flow.
/// </summary>
public interface IMyCampaignService
{
    Task<IReadOnlyList<MyCampaignSummaryDto>> GetMyCampaignsAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CampaignCustomerDto>> GetMyCustomersAsync(
        Guid campaignId,
        Guid currentUserId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CampaignCustomerDto> UpdateContactAsync(
        Guid campaignId,
        Guid customerId,
        Guid currentUserId,
        UpdateCampaignCustomerContactRequestDto request,
        CancellationToken cancellationToken = default);
}
