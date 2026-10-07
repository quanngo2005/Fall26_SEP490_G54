using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class PerformanceContribution
{
    public Guid Id { get; set; }

    public Guid PerformanceEvaluationId { get; set; }

    public Guid? ParentContributionId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid EvaluationPeriodId { get; set; }

    public Guid? KpiDefinitionId { get; set; }

    public Guid? EvaluationProfileItemId { get; set; }

    public Guid? TargetAssignmentId { get; set; }

    public decimal? TargetValueSnapshot { get; set; }

    public int? TargetVersionSnapshot { get; set; }

    public decimal? ActualValue { get; set; }

    public decimal? ActualDenominator { get; set; }

    public decimal? AchievementPercentRaw { get; set; }

    public decimal? AchievementPercentCapped { get; set; }

    public decimal? WeightSnapshot { get; set; }

    public decimal? ComponentScore { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual ICollection<ActualProgress> ActualProgresses { get; set; } = new List<ActualProgress>();

    public virtual Employee Employee { get; set; } = null!;

    public virtual EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    public virtual EvaluationProfileItem? EvaluationProfileItem { get; set; }

    public virtual ICollection<PerformanceContribution> InverseParentContribution { get; set; } = new List<PerformanceContribution>();

    public virtual KpiDefinition? KpiDefinition { get; set; }

    public virtual PerformanceContribution? ParentContribution { get; set; }

    public virtual PerformanceEvaluation PerformanceEvaluation { get; set; } = null!;

    public virtual PerformanceEvaluation PerformanceEvaluationNavigation { get; set; } = null!;

    public virtual TargetAssignment? TargetAssignment { get; set; }
}
