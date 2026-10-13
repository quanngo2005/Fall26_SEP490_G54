using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class RiskEvent
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public string Code { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public string Description { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public RiskEventSeverity Severity { get; set; }
    public RiskEventStatus Status { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<Evidence> Evidences { get; set; } = new List<Evidence>();

    public virtual VerifiedWorkResult? VerifiedWorkResult { get; set; }
}
