using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class VerifiedWorkResult
{
    public Guid Id { get; set; }

    public Guid VerificationId { get; set; }

    public Guid? InvalidatedByRiskEventId { get; set; }

    public DateTimeOffset VerifiedAt { get; set; }

    public DateTimeOffset? InvalidatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public VerifiedResultStatus Status { get; set; }

    public virtual RiskEvent? InvalidatedByRiskEvent { get; set; }

    public virtual Verification Verification { get; set; } = null!;

    public virtual ICollection<WorkResultItem> WorkResultItems { get; set; } = new List<WorkResultItem>();
}
