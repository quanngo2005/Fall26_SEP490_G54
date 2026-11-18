using System;
using System.Collections.Generic;
using NpgsqlTypes;

namespace G54.DAL.Entities;

/// <summary>Whose work earns the KPI: the employee who handled the transaction, or the one referred by its Refer ID. Stored as <see cref="PgNameAttribute"/> text.</summary>
public enum KpiCreditRole
{
    [PgName("PROCESSOR")] Processor,
    [PgName("REFERRER")] Referrer,
}

/// <summary>Which imported file the KPI is measured from.</summary>
public enum KpiDataSource
{
    [PgName("TRANSACTION")] Transaction,
    [PgName("SERVICE_RATING")] ServiceRating,
}

public partial class KpiDefinition
{
    public Guid Id { get; set; }

    public Guid KpiGroupId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Formula { get; set; }

    public KpiCreditRole CreditRole { get; set; } = KpiCreditRole.Processor;

    public KpiDataSource DataSource { get; set; } = KpiDataSource.Transaction;

    /// <summary>The product the KPI belongs to (rows are grouped by it); null for a KPI that is not tied to a product.</summary>
    public string? ProductName { get; set; }

    public decimal? BaselineValue { get; set; }

    /// <summary>First period the KPI applies to; null for a KPI that has always applied.</summary>
    public Guid? EffectiveFromPeriodId { get; set; }

    /// <summary>Last period the KPI applies to; null means no end.</summary>
    public Guid? EffectiveToPeriodId { get; set; }

    /// <summary>When the System Admin sent the KPI for approval.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

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

    public virtual EvaluationPeriod? EffectiveFromPeriod { get; set; }

    public virtual EvaluationPeriod? EffectiveToPeriod { get; set; }

    /// <summary>The job positions the KPI applies to.</summary>
    public virtual ICollection<JobPosition> JobPositions { get; set; } = new List<JobPosition>();

    public virtual ICollection<EvaluationProfileItem> EvaluationProfileItems { get; set; } = new List<EvaluationProfileItem>();

    public virtual ICollection<ObjectiveKpi> ObjectiveKpis { get; set; } = new List<ObjectiveKpi>();

    public virtual ICollection<TargetAssignment> TargetAssignments { get; set; } = new List<TargetAssignment>();

    public virtual ICollection<WorkTypeKpi> WorkTypeKpis { get; set; } = new List<WorkTypeKpi>();
}
