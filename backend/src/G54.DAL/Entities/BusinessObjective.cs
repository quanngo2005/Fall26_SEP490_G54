using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class BusinessObjective
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public BusinessObjectiveStatus Status { get; set; }

    public virtual ICollection<BusinessCampaign> BusinessCampaigns { get; set; } = new List<BusinessCampaign>();

    public virtual ICollection<ObjectiveKpi> ObjectiveKpis { get; set; } = new List<ObjectiveKpi>();
}
