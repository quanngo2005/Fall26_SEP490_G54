using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Shared lead-Work helpers (CAMPAIGN_CONTRACT.md §5.6). A lead Work is a Work referenced by
/// <c>campaign_customer.work_id</c>. Used by allocate/reassign (Step 3), contact update (Step 4) and the lock
/// checks on the generic Work endpoints (Step 5).
/// </summary>
internal static class CampaignLeadWork
{
    /// <summary>
    /// Returns the contact status of the customer that references <paramref name="workId"/> as its lead Work, or
    /// null when <paramref name="workId"/> is not a lead Work. One indexed lookup on <c>uq_campaign_customer_work</c>.
    /// </summary>
    public static async Task<CampaignContactStatus?> LeadStatusAsync(
        IQueryable<CampaignCustomer> customers, Guid workId, CancellationToken cancellationToken)
    {
        return await customers
            .Where(c => c.WorkId == workId)
            .Select(c => (CampaignContactStatus?)c.ContactStatus)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Builds a new lead Work for <paramref name="customer"/> at allocation time (CAMPAIGN_CONTRACT.md §5.6).</summary>
    public static Work Build(
        BusinessCampaign campaign,
        WorkType workType,
        CampaignCustomer customer,
        Guid creatorId,
        string code,
        DateOnly today,
        DateTimeOffset now)
    {
        return new Work
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = $"[Chiến dịch {campaign.Code}] {customer.CustomerName}",
            Description = Description(campaign, customer, workType, null),
            BusinessCampaignId = campaign.Id,
            WorkTypeId = workType.Id,
            AssigneeEmployeeId = customer.AssigneeEmployeeId
                ?? throw new InvalidOperationException("Customer must be assigned before a lead Work can be built."),
            CreatedBy = creatorId,
            CustomerCif = customer.CustomerCif,
            CustomerName = customer.CustomerName,
            Status = WorkStatus.New,
            Priority = WorkPriority.Medium,
            PlannedStartDate = today,
            PlannedEndDate = null,
            AssignedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// The lead Work description (CAMPAIGN_CONTRACT.md §5.5): "Nguồn: chiến dịch {code} - {name}. Mã KH: {cif},
    /// Tên KH: {customerName}. Sản phẩm: {workTypeName}." followed by " Ghi chú: {note}" from the customer note or,
    /// when given, <paramref name="closeNote"/> (the note typed when closing). Replaces the former
    /// <c>MyCampaignService.BuildClosedWorkDescription</c>.
    /// </summary>
    public static string Description(BusinessCampaign campaign, CampaignCustomer customer, WorkType workType, string? closeNote)
    {
        var description = $"Nguồn: chiến dịch {campaign.Code} - {campaign.Name}. Mã KH: {customer.CustomerCif}, Tên KH: {customer.CustomerName}. Sản phẩm: {workType.Name}.";
        var note = closeNote ?? customer.Note;
        return string.IsNullOrWhiteSpace(note) ? description : $"{description} Ghi chú: {note}";
    }
}
