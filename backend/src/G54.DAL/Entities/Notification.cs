using NpgsqlTypes;

namespace G54.DAL.Entities;

/// <summary>Stored as the <see cref="PgNameAttribute"/> text in a varchar column (not a PostgreSQL enum type).</summary>
public enum NotificationType
{
    [PgName("ANNOUNCEMENT")] Announcement,
    [PgName("HANDOVER_REQUESTED")] HandoverRequested,
    [PgName("HANDOVER_ACCEPTED")] HandoverAccepted,
    [PgName("HANDOVER_REJECTED")] HandoverRejected,
    [PgName("HANDOVER_TRANSFERRED")] HandoverTransferred,
    [PgName("WORK_OVERDUE")] WorkOverdue,
    [PgName("KPI_APPROVED")] KpiApproved,
    [PgName("KPI_REJECTED")] KpiRejected,
}

public enum NotificationSeverity
{
    [PgName("NORMAL")] Normal,
    [PgName("IMPORTANT")] Important,
}

/// <summary>What a notification links to, so the client can open it.</summary>
public enum NotificationRelatedType
{
    [PgName("WORK")] Work,
    [PgName("CAMPAIGN")] Campaign,
    [PgName("HANDOVER")] Handover,
    [PgName("KPI")] Kpi,
}

/// <summary>One notification for one recipient; an announcement to many employees is one row each, so read state is per person.</summary>
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientEmployeeId { get; set; }

    /// <summary>The employee who sent an announcement; null for notifications raised by the system.</summary>
    public Guid? SenderEmployeeId { get; set; }

    public NotificationType Type { get; set; }
    public NotificationSeverity Severity { get; set; }
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
    public NotificationRelatedType? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null while unread.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    public Employee? Sender { get; set; }
}
