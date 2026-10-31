using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class PerformanceEvaluation
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid EvaluationPeriodId { get; set; }

    public Guid EvaluationProfileId { get; set; }

    public Guid? ParentEvaluationId { get; set; }

    public decimal? TotalScore { get; set; }

    public DateTimeOffset? CalculatedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public PerformanceEvaluationStatus Status { get; set; }

    /// <summary>
    /// Status held before a dispute froze the score (BR-31); restored when the last dispute is withdrawn.
    /// Only set while <see cref="Status"/> is <see cref="PerformanceEvaluationStatus.Disputed"/>.
    /// </summary>
    public PerformanceEvaluationStatus? PreDisputeStatus { get; set; }

    /// <summary>When the score became disputed (pending); null otherwise.</summary>
    public DateTimeOffset? DisputedAt { get; set; }

    public virtual ICollection<Appeal> Appeals { get; set; } = new List<Appeal>();

    public virtual Employee Employee { get; set; } = null!;

    public virtual EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    public virtual EvaluationProfile EvaluationProfile { get; set; } = null!;

    public virtual ICollection<PerformanceEvaluation> InverseParentEvaluation { get; set; } = new List<PerformanceEvaluation>();

    public virtual PerformanceEvaluation? ParentEvaluation { get; set; }

    public virtual ICollection<PerformanceEvaluationHistory> Histories { get; set; } = new List<PerformanceEvaluationHistory>();

    public virtual ICollection<PerformanceContribution> PerformanceContributions { get; set; } = new List<PerformanceContribution>();
}
