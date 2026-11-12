namespace G54.DAL.Entities;

/// <summary>One entry of a work's activity log (SCR-WRK-03). Append-only: entries are never edited or removed.</summary>
public sealed class WorkActivity
{
    public Guid Id { get; set; }

    public Guid WorkId { get; set; }

    /// <summary>Null when the system acted (for example the overdue job).</summary>
    public Guid? ActorEmployeeId { get; set; }

    /// <summary>A stable code such as <c>ASSIGNED</c>, <c>STATUS_CHANGED</c>, <c>CHECKLIST_UPDATED</c>, <c>PLAN_UPDATED</c>.</summary>
    public string Action { get; set; } = null!;

    /// <summary>Human readable text shown in the log (Vietnamese).</summary>
    public string Message { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public Work Work { get; set; } = null!;

    public Employee? ActorEmployee { get; set; }
}
