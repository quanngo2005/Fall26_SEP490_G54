using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class TargetAssignment
{
    public Guid Id { get; set; }

    public Guid? BusinessObjectiveId { get; set; }

    public Guid KpiDefinitionId { get; set; }

    public Guid? DepartmentId { get; set; }

    public Guid? EmployeeId { get; set; }

    public Guid EvaluationPeriodId { get; set; }

    public Guid? ParentAssignmentId { get; set; }

    public int Version { get; set; }

    public decimal TargetValue { get; set; }

    public DateTimeOffset? AssignedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ChangeReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public TargetAssignmentStatus Status { get; set; }

    public TargetSplitDimension? SplitDimension { get; set; }

    /// <summary>First day this version applies (BR-15). Results dated before it are scored against the previous version.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Last day this version applies; set when a later version freezes it.</summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>
    /// The version this one replaced mid-period (BR-15). Not a database foreign key: a self-referencing FK on this
    /// table makes PostgreSQL unable to run further ALTER TABLE statements against it later in the same migration
    /// transaction (error 55006, "pending trigger events") once that FK is dropped, which breaks the test suite's
    /// migration-rollback tests; the ix_target_assignment_previous_version index still indexes it.
    /// </summary>
    public Guid? PreviousVersionId { get; set; }

    public Guid? ChangedByEmployeeId { get; set; }

    public virtual ICollection<ActualProgress> ActualProgresses { get; set; } = new List<ActualProgress>();

    public virtual BusinessObjective? BusinessObjective { get; set; }

    public virtual Department? Department { get; set; }

    public virtual Employee? Employee { get; set; }

    public virtual EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    public virtual ICollection<TargetAssignment> InverseParentAssignment { get; set; } = new List<TargetAssignment>();

    public virtual KpiDefinition KpiDefinition { get; set; } = null!;

    public virtual TargetAssignment? ParentAssignment { get; set; }

    public virtual Employee? ChangedByEmployee { get; set; }
}
