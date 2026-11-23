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

    /// <summary>Hard-lock watermark (BR-29): every month up to and including this date's month is read-only.</summary>
    public DateOnly? HardLockedThrough { get; set; }

    /// <summary>Frozen JSON snapshot of the <see cref="RankingPolicy"/> definition captured when the period is locked (GB-910).</summary>
    public string? RankingPolicySnapshotJson { get; set; }

    public virtual ICollection<EvaluationPeriod> InverseParentPeriod { get; set; } = new List<EvaluationPeriod>();

    public virtual EvaluationPeriod? ParentPeriod { get; set; }

    public virtual ICollection<ResultSubmission> CarriedOverResultSubmissions { get; set; } = new List<ResultSubmission>();

    public virtual ICollection<PerformanceContribution> PerformanceContributions { get; set; } = new List<PerformanceContribution>();

    public virtual ICollection<PerformanceEvaluation> PerformanceEvaluations { get; set; } = new List<PerformanceEvaluation>();

    public virtual ICollection<TargetAssignment> TargetAssignments { get; set; } = new List<TargetAssignment>();
}
