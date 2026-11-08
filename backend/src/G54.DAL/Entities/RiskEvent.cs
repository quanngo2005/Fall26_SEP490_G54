using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class RiskEvent
{
    public Guid Id { get; set; }

    /// <summary>Number shown as <c>RSK-0045</c> (sequence <c>risk_event_code_seq</c>); <see cref="Code"/> stays the unique system code.</summary>
    public long CodeNo { get; set; }

    /// <summary>Penalty the recorder entered, always positive. Null: the penalty follows the severity.</summary>
    public decimal? PenaltyPoints { get; set; }

    /// <summary>The work the event is linked to, if any.</summary>
    public Guid? WorkId { get; set; }

    /// <summary>When the event was recorded (the violation date is <see cref="OccurredAt"/>).</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Set when a recorded event was reversed: it then counts as <c>DISMISSED</c> and keeps who, when and why.</summary>
    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public virtual Work? Work { get; set; }

    public virtual Employee? ReversedByEmployee { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid RecordedBy { get; set; }

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

    public virtual PerformanceContribution? PerformanceContribution { get; set; }

    public virtual Employee RecordedByNavigation { get; set; } = null!;
}
