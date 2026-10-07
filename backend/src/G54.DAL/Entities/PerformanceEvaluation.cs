using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class PerformanceEvaluation
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid EvaluationPeriodId { get; set; }

    public Guid EvaluationProfileId { get; set; }

    public decimal? TotalScore { get; set; }

    public Guid? ReviewerEmployeeId { get; set; }

    public DateTimeOffset? CalculatedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public PerformanceEvaluationStatus Status { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    public virtual EvaluationProfile EvaluationProfile { get; set; } = null!;

    public virtual ICollection<PerformanceContribution> PerformanceContributionPerformanceEvaluationNavigations { get; set; } = new List<PerformanceContribution>();

    public virtual ICollection<PerformanceContribution> PerformanceContributionPerformanceEvaluations { get; set; } = new List<PerformanceContribution>();

    public virtual Employee? ReviewerEmployee { get; set; }
}
