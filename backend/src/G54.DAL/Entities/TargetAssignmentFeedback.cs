using System;

namespace G54.DAL.Entities;

/// <summary>Employee confirmation or comment on a target assignment. Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.</summary>
public partial class TargetAssignmentFeedback
{
    public Guid Id { get; set; }

    public Guid TargetAssignmentId { get; set; }

    public Guid EmployeeId { get; set; }

    public TargetAssignmentFeedbackType Type { get; set; }

    public string? Comment { get; set; }

    /// <summary>Employee's proposed year value for a mid-term feedback (Target Allocation v3, my-targets feedback). Null for a plain comment/confirm.</summary>
    public decimal? ProposedYear { get; set; }

    public decimal? ProposedQ1 { get; set; }

    public decimal? ProposedQ2 { get; set; }

    public decimal? ProposedQ3 { get; set; }

    public decimal? ProposedQ4 { get; set; }

    /// <summary>DM's reply to the employee's feedback (&lt;= 500 chars); null until replied.</summary>
    public string? Reply { get; set; }

    public Guid? RepliedBy { get; set; }

    public DateTimeOffset? RepliedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual TargetAssignment TargetAssignment { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;
}
