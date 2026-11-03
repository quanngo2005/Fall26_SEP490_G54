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

    /// <summary>Why the approver rejected the KPI; set while <see cref="KpiStatus.Rejected"/>, cleared on resubmission.</summary>
    public string? RejectReason { get; set; }

    /// <summary>What the Branch Manager asked the System Admin to add; set only for a KPI created through a request.</summary>
    public string? RequestNote { get; set; }

    /// <summary>The Branch Manager who requested the KPI; null when the System Admin created it directly.</summary>
    public Guid? RequestedBy { get; set; }

    /// <summary>The employee who sent the KPI for approval (the maker); the approver must be someone else.</summary>
    public Guid? SubmittedBy { get; set; }

    public virtual KpiGroup KpiGroup { get; set; } = null!;

    public virtual ICollection<EvaluationProfileItem> EvaluationProfileItems { get; set; } = new List<EvaluationProfileItem>();

    public virtual ICollection<ObjectiveKpi> ObjectiveKpis { get; set; } = new List<ObjectiveKpi>();

    public virtual ICollection<TargetAssignment> TargetAssignments { get; set; } = new List<TargetAssignment>();

    public virtual ICollection<WorkTypeKpi> WorkTypeKpis { get; set; } = new List<WorkTypeKpi>();
}
