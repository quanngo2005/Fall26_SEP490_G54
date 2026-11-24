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
    [PgName("REWORK_REQUESTED")] ReworkRequested,
    [PgName("TARGET_REVISED")] TargetRevised,
    [PgName("RATING_ONE_STAR")] RatingOneStar,
    [PgName("RESULT_PUBLISHED")] ResultPublished,
    [PgName("WORK_PLAN_UPDATED")] WorkPlanUpdated,
    [PgName("RESULT_CARRIED_OVER")] ResultCarriedOver,
    [PgName("WORK_ASSIGNED")] WorkAssigned,
    [PgName("SUBMISSION_SUBMITTED")] SubmissionSubmitted,
    [PgName("SUBMISSION_DECIDED")] SubmissionDecided,
    [PgName("TRANSACTION_MISMATCH")] TransactionMismatch,
    [PgName("IMPORT_COMPLETED_WITH_ERRORS")] ImportCompletedWithErrors,
    [PgName("RISK_EVENT_RECORDED")] RiskEventRecorded,
    [PgName("RISK_EVENT_REVERSED")] RiskEventReversed,
    [PgName("QUALITY_FLAG_RAISED")] QualityFlagRaised,
    [PgName("APPEAL_FILED")] AppealFiled,
    [PgName("APPEAL_DECIDED")] AppealDecided,
    [PgName("CALIBRATION_SUBMITTED")] CalibrationSubmitted,
    [PgName("CALIBRATION_DECIDED")] CalibrationDecided,
    [PgName("CAMPAIGN_DECIDED")] CampaignDecided,
    [PgName("EVALUATION_PROFILE_DECIDED")] EvaluationProfileDecided,
    [PgName("CAMPAIGN_END_REMINDER")] CampaignEndReminder,
    [PgName("SYSTEM_ALERT")] SystemAlert,
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
    [PgName("TARGET")] Target,
    [PgName("SUBMISSION")] Submission,
    [PgName("APPEAL")] Appeal,
    [PgName("RISK_EVENT")] RiskEvent,
    [PgName("QUALITY_FLAG")] QualityFlag,
    [PgName("EVALUATION_PERIOD")] EvaluationPeriod,
    [PgName("EVALUATION_PROFILE")] EvaluationProfile,
    [PgName("IMPORT_BATCH")] ImportBatch,
    [PgName("AUDIT_LOG")] AuditLog,
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
