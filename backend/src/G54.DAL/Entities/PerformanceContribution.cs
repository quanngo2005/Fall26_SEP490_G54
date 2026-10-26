using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class PerformanceContribution
{
    public Guid Id { get; set; }

    public Guid? PerformanceEvaluationId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid EvaluationPeriodId { get; set; }

    public Guid? ActualProgressId { get; set; }

    public Guid? RiskEventId { get; set; }

    public decimal? TargetValueSnapshot { get; set; }

    public int? TargetVersionSnapshot { get; set; }

    public decimal? ActualValue { get; set; }

    public decimal? ActualDenominator { get; set; }

    public decimal? AchievementPercentRaw { get; set; }

    public decimal? AchievementPercentCapped { get; set; }

    public decimal? WeightSnapshot { get; set; }

    public decimal? ComponentScore { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual ActualProgress? ActualProgress { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    public virtual PerformanceEvaluation? PerformanceEvaluation { get; set; }

    public virtual RiskEvent? RiskEvent { get; set; }
}
