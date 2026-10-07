using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class BusinessCampaign
{
    public Guid Id { get; set; }

    public Guid BusinessObjectiveId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public BusinessCampaignStatus Status { get; set; }

    public virtual BusinessObjective BusinessObjective { get; set; } = null!;

    public virtual ICollection<CampaignWorkType> CampaignWorkTypes { get; set; } = new List<CampaignWorkType>();

    public virtual ICollection<Participant> Participants { get; set; } = new List<Participant>();

    public virtual ICollection<Work> Works { get; set; } = new List<Work>();
}
