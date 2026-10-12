using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class Participant
{
    public Guid Id { get; set; }

    public Guid BusinessCampaignId { get; set; }

    public Guid EmployeeId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual BusinessCampaign BusinessCampaign { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;
}
