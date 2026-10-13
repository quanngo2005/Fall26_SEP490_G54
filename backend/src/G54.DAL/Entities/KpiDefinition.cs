using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class KpiDefinition
{
    public Guid Id { get; set; }

    public Guid KpiGroupId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Formula { get; set; }

    public string Unit { get; set; } = null!;

    public decimal? MaxAchievementPercent { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public KpiAggregationType AggregationType { get; set; }
    public KpiDirection Direction { get; set; }
    public KpiStatus Status { get; set; }

    public virtual ICollection<ActualProgress> ActualProgresses { get; set; } = new List<ActualProgress>();

    public virtual KpiGroup KpiGroup { get; set; } = null!;

    public virtual ICollection<ObjectiveKpi> ObjectiveKpis { get; set; } = new List<ObjectiveKpi>();

    public virtual ICollection<PerformanceContribution> PerformanceContributions { get; set; } = new List<PerformanceContribution>();

    public virtual ICollection<TargetAssignment> TargetAssignments { get; set; } = new List<TargetAssignment>();

    public virtual ICollection<WorkResultItem> WorkResultItems { get; set; } = new List<WorkResultItem>();

    public virtual ICollection<WorkTypeKpi> WorkTypeKpis { get; set; } = new List<WorkTypeKpi>();
}
