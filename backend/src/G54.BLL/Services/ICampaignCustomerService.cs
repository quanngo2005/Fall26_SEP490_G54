using G54.BLL.Dtos.CampaignCustomer;

namespace G54.BLL.Services;

/// <summary>
/// Manager-side campaign customer operations: list with filters/summary, auto allocation, and manual reassignment.
/// Contract: docs/api/CAMPAIGN_CONTRACT.md §5.
/// </summary>
public interface ICampaignCustomerService
{
    Task<CampaignCustomerListResultDto> ListAsync(
        Guid campaignId,
        string? search,
        string? status,
        Guid? assigneeId,
        bool unassignedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AllocateCampaignCustomersResultDto> AllocateAsync(
        Guid campaignId,
        string? rule,
        IReadOnlyList<Guid>? employeeIds,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<CampaignCustomerDto> ReassignAsync(
        Guid campaignId,
        Guid customerId,
        Guid employeeId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
