using G54.BLL.Common.Models;

namespace G54.BLL.Dtos.CampaignCustomer;

// =========================================================================
// Campaign customers - manager side (docs/api/CAMPAIGN_CONTRACT.md §5)
// =========================================================================

public sealed record CampaignCustomerDto(
    Guid Id,
    string CustomerCif,
    string CustomerName,
    string? Phone,
    string? Note,
    Guid? AssigneeEmployeeId,
    string? AssigneeEmployeeName,
    string ContactStatus,
    string ResultText,
    int MissedCallCount,
    DateTimeOffset? AppointmentAt,
    DateTimeOffset? MetAt,
    Guid? ClosedWorkTypeId,
    string? ClosedWorkTypeName,
    Guid? WorkId,
    DateTimeOffset UpdatedAt);

public sealed record CampaignCustomerStatusCountDto(string Status, int Count);

public sealed record CampaignCustomerAssigneeSummaryDto(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    int Assigned,
    int Closed);

public sealed record CampaignCustomerSummaryDto(
    int Total,
    int Unassigned,
    IReadOnlyList<CampaignCustomerStatusCountDto> ByStatus,
    IReadOnlyList<CampaignCustomerAssigneeSummaryDto> ByAssignee);

public sealed record CampaignCustomerListResultDto(
    PagedResult<CampaignCustomerDto> Page,
    CampaignCustomerSummaryDto Summary);

public sealed record CampaignCustomerAllocationCountDto(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    int Count);

public sealed record AllocateCampaignCustomersResultDto(
    int AssignedCount,
    IReadOnlyList<CampaignCustomerAllocationCountDto> ByEmployee);

// ---- requests ----------------------------------------------------------------------------------------------

public sealed record AllocateCampaignCustomersRequestDto(
    string? Rule,
    IReadOnlyList<Guid>? EmployeeIds);

public sealed record ReassignCampaignCustomerRequestDto(Guid EmployeeId);

// =========================================================================
// Campaign customers - employee side (docs/api/CAMPAIGN_CONTRACT.md §5, "My campaigns")
// =========================================================================

public sealed record MyCampaignSummaryDto(
    Guid CampaignId,
    string CampaignCode,
    string CampaignName,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalAssigned,
    int Closed,
    IReadOnlyList<CampaignCustomerStatusCountDto> ByStatus);

public sealed record UpdateCampaignCustomerContactRequestDto(
    string Status,
    bool? CallAnswered,
    DateTimeOffset? AppointmentAt,
    DateTimeOffset? MetAt,
    Guid? WorkTypeId);
