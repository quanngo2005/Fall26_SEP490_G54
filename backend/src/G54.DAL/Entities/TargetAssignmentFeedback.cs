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

    public DateTimeOffset CreatedAt { get; set; }

    public virtual TargetAssignment TargetAssignment { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;
}
