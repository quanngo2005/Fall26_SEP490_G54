using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class EvaluationPeriod
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public Guid? ParentPeriodId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public PeriodType PeriodType { get; set; }
    public PeriodStatus Status { get; set; }

    public virtual ICollection<ActualProgress> ActualProgresses { get; set; } = new List<ActualProgress>();

    public virtual ICollection<EvaluationPeriod> InverseParentPeriod { get; set; } = new List<EvaluationPeriod>();

    public virtual EvaluationPeriod? ParentPeriod { get; set; }

    public virtual ICollection<PerformanceContribution> PerformanceContributions { get; set; } = new List<PerformanceContribution>();

    public virtual ICollection<PerformanceEvaluation> PerformanceEvaluations { get; set; } = new List<PerformanceEvaluation>();

    public virtual ICollection<TargetAssignment> TargetAssignments { get; set; } = new List<TargetAssignment>();
}
