using System;

namespace G54.DAL.Entities;

public partial class CampaignCustomer : IBaseEntity
{
    public Guid Id { get; set; }

    public Guid BusinessCampaignId { get; set; }

    public string CustomerCif { get; set; } = null!;

    public string CustomerName { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Note { get; set; }

    public Guid? AssigneeEmployeeId { get; set; }

    public DateTimeOffset? AssignedAt { get; set; }

    public CampaignContactStatus ContactStatus { get; set; }

    public bool? CallAnswered { get; set; }

    public int MissedCallCount { get; set; }

    public DateTimeOffset? AppointmentAt { get; set; }

    public DateTimeOffset? MetAt { get; set; }

    public Guid? ClosedWorkTypeId { get; set; }

    public Guid? WorkId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public virtual BusinessCampaign BusinessCampaign { get; set; } = null!;

    public virtual Employee? AssigneeEmployee { get; set; }

    public virtual WorkType? ClosedWorkType { get; set; }

    public virtual Work? Work { get; set; }
}
