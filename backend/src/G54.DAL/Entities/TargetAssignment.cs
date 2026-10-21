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

    public virtual ICollection<ActualProgress> ActualProgresses { get; set; } = new List<ActualProgress>();

    public virtual BusinessObjective? BusinessObjective { get; set; }

    public virtual Department? Department { get; set; }

    public virtual Employee? Employee { get; set; }

    public virtual EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    public virtual ICollection<TargetAssignment> InverseParentAssignment { get; set; } = new List<TargetAssignment>();

    public virtual KpiDefinition KpiDefinition { get; set; } = null!;

    public virtual TargetAssignment? ParentAssignment { get; set; }
}
