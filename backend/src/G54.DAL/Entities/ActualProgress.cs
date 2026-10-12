using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class ActualProgress
{
    public Guid Id { get; set; }

    public Guid KpiDefinitionId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid EvaluationPeriodId { get; set; }

    public DateOnly ProgressDate { get; set; }

    public decimal Value { get; set; }

    public decimal? Denominator { get; set; }

    public Guid? SourceItemId { get; set; }

    public Guid? PerformanceContributionId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ActualProgressStatus Status { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    public virtual KpiDefinition KpiDefinition { get; set; } = null!;

    public virtual PerformanceContribution? PerformanceContribution { get; set; }

    public virtual WorkResultItem? SourceItem { get; set; }
}
