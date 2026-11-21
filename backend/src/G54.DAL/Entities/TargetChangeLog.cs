using System;

namespace G54.DAL.Entities;

/// <summary>Who/what kind of action produced a target value change (D10).</summary>
public static class TargetChangeLogAction
{
    public const string Suggest = "SUGGEST";
    public const string Manual = "MANUAL";
    public const string Publish = "PUBLISH";
    public const string Adjust = "ADJUST";
    public const string Feedback = "FEEDBACK";
    public const string Reply = "REPLY";
}

/// <summary>
/// Structured, queryable log of target value changes (D10), beyond the generic <see cref="AuditLog"/>. Used to show
/// an employee the "changed KPIs" (old/new value) that caused their ack to move to RECONFIRM, and to record
/// suggestion/manual/publish/adjustment/feedback/reply actions for Target Allocation v3.
/// </summary>
public sealed class TargetChangeLog
{
    public Guid Id { get; set; }

    /// <summary>Who performed the action (BM, DM, or the employee for FEEDBACK/REPLY).</summary>
    public Guid ActorId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>One of <see cref="TargetChangeLogAction"/>.</summary>
    public string Action { get; set; } = null!;

    /// <summary>1 = branch, 2 = department, 3 = employee.</summary>
    public int Tier { get; set; }

    public Guid KpiDefinitionId { get; set; }

    public string Unit { get; set; } = null!;

    /// <summary>"YEAR", "Q1".."Q4".</summary>
    public string Period { get; set; } = null!;

    public decimal? OldValue { get; set; }

    public decimal? NewValue { get; set; }

    /// <summary>Suggestion basis, e.g. TARGET_LAST_YEAR / ACTUAL_LAST_YEAR / MANUAL; null when not applicable.</summary>
    public string? Basis { get; set; }

    public decimal? RoundingStep { get; set; }

    /// <summary>Employee this change affects, when scoped to tier 3; null for branch/department-level entries.</summary>
    public Guid? EmployeeId { get; set; }

    public Guid? DepartmentId { get; set; }

    public int? Year { get; set; }

    /// <summary>Extra scope metadata as JSON (e.g. feedbackId); nullable.</summary>
    public string? ScopeJson { get; set; }

    public KpiDefinition KpiDefinition { get; set; } = null!;

    public Employee? Employee { get; set; }

    public Department? Department { get; set; }
}
