using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class CampaignWorkType
{
    public Guid Id { get; set; }

    public Guid BusinessCampaignId { get; set; }

    public Guid WorkTypeId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual BusinessCampaign BusinessCampaign { get; set; } = null!;

    public virtual WorkType WorkType { get; set; } = null!;
}
