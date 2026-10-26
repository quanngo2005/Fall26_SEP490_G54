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

    public virtual KpiGroup KpiGroup { get; set; } = null!;

    public virtual ICollection<ObjectiveKpi> ObjectiveKpis { get; set; } = new List<ObjectiveKpi>();

    public virtual ICollection<TargetAssignment> TargetAssignments { get; set; } = new List<TargetAssignment>();

    public virtual ICollection<WorkTypeKpi> WorkTypeKpis { get; set; } = new List<WorkTypeKpi>();
}
