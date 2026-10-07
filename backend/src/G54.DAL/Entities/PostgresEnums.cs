using NpgsqlTypes;

namespace G54.DAL.Entities;

public enum DepartmentLevelType
{
    [PgName("BRANCH")] Branch,
    [PgName("DEPARTMENT")] Department,
    [PgName("TEAM")] Team,
}

public enum EmployeeStatus
{
    [PgName("ACTIVE")] Active,
    [PgName("INACTIVE")] Inactive,
    [PgName("ON_LEAVE")] OnLeave,
    [PgName("TERMINATED")] Terminated,
}

public enum RecordStatus
{
    [PgName("ACTIVE")] Active,
    [PgName("INACTIVE")] Inactive,
    [PgName("ARCHIVED")] Archived,
}

public enum BusinessObjectiveStatus
{
    [PgName("DRAFT")] Draft,
    [PgName("ACTIVE")] Active,
    [PgName("CLOSED")] Closed,
    [PgName("CANCELLED")] Cancelled,
}

public enum BusinessCampaignStatus
{
    [PgName("PLANNED")] Planned,
    [PgName("RUNNING")] Running,
    [PgName("PAUSED")] Paused,
    [PgName("COMPLETED")] Completed,
    [PgName("CANCELLED")] Cancelled,
}

public enum KpiStatus
{
    [PgName("DRAFT")] Draft,
    [PgName("ACTIVE")] Active,
    [PgName("INACTIVE")] Inactive,
    [PgName("ARCHIVED")] Archived,
}

public enum KpiAggregationType
{
    [PgName("SUM")] Sum,
    [PgName("RATIO")] Ratio,
    [PgName("AVG")] Avg,
    [PgName("NO_AGGREGATION")] NoAggregation,
}

public enum KpiDirection
{
    [PgName("HIGHER_BETTER")] HigherBetter,
    [PgName("LOWER_BETTER")] LowerBetter,
    [PgName("ON_TARGET")] OnTarget,
}

public enum WorkPriority
{
    [PgName("LOW")] Low,
    [PgName("MEDIUM")] Medium,
    [PgName("HIGH")] High,
    [PgName("CRITICAL")] Critical,
}

public enum WorkStatus
{
    [PgName("NEW")] New,
    [PgName("IN_PROGRESS")] InProgress,
    [PgName("HANDED_OVER")] HandedOver,
    [PgName("SUBMITTED")] Submitted,
    [PgName("VERIFIED")] Verified,
    [PgName("REJECTED")] Rejected,
    [PgName("DONE")] Done,
    [PgName("CANCELLED")] Cancelled,
}

public enum WorkflowDefinitionStatus
{
    [PgName("DRAFT")] Draft,
    [PgName("ACTIVE")] Active,
    [PgName("INACTIVE")] Inactive,
    [PgName("ARCHIVED")] Archived,
}

public enum HandoverStatus
{
    [PgName("PENDING")] Pending,
    [PgName("ACCEPTED")] Accepted,
    [PgName("REJECTED")] Rejected,
    [PgName("CANCELLED")] Cancelled,
}

public enum SubmissionStatus
{
    [PgName("DRAFT")] Draft,
    [PgName("SUBMITTED")] Submitted,
    [PgName("UNDER_REVIEW")] UnderReview,
    [PgName("APPROVED")] Approved,
    [PgName("REJECTED")] Rejected,
    [PgName("WITHDRAWN")] Withdrawn,
}

public enum VerificationDecision
{
    [PgName("PENDING")] Pending,
    [PgName("APPROVED")] Approved,
    [PgName("REJECTED")] Rejected,
    [PgName("NEED_MORE_INFO")] NeedMoreInfo,
}

public enum VerifiedResultStatus
{
    [PgName("VALID")] Valid,
    [PgName("INVALID")] Invalid,
}

public enum ActualProgressStatus
{
    [PgName("VALID")] Valid,
    [PgName("INVALID")] Invalid,
}

public enum RiskEventSeverity
{
    [PgName("LOW")] Low,
    [PgName("MEDIUM")] Medium,
    [PgName("HIGH")] High,
    [PgName("CRITICAL")] Critical,
}

public enum RiskEventStatus
{
    [PgName("OPEN")] Open,
    [PgName("CONFIRMED")] Confirmed,
    [PgName("DISMISSED")] Dismissed,
    [PgName("RESOLVED")] Resolved,
}

public enum AppealStatus
{
    [PgName("OPEN")] Open,
    [PgName("UNDER_REVIEW")] UnderReview,
    [PgName("ACCEPTED")] Accepted,
    [PgName("REJECTED")] Rejected,
    [PgName("WITHDRAWN")] Withdrawn,
}

public enum PeriodType
{
    [PgName("YEAR")] Year,
    [PgName("HALF_YEAR")] HalfYear,
    [PgName("QUARTER")] Quarter,
    [PgName("MONTH")] Month,
    [PgName("DAY")] Day,
}

public enum PeriodStatus
{
    [PgName("OPEN")] Open,
    [PgName("IN_REVIEW")] InReview,
    [PgName("LOCKED")] Locked,
    [PgName("CLOSED")] Closed,
}

public enum EvaluationProfileStatus
{
    [PgName("DRAFT")] Draft,
    [PgName("ACTIVE")] Active,
    [PgName("INACTIVE")] Inactive,
    [PgName("ARCHIVED")] Archived,
}

public enum PerformanceEvaluationStatus
{
    [PgName("DRAFT")] Draft,
    [PgName("CALCULATED")] Calculated,
    [PgName("IN_REVIEW")] InReview,
    [PgName("APPROVED")] Approved,
    [PgName("DISPUTED")] Disputed,
    [PgName("FINAL")] Final,
}

public enum TargetAssignmentStatus
{
    [PgName("DRAFT")] Draft,
    [PgName("ACTIVE")] Active,
    [PgName("SUPERSEDED")] Superseded,
    [PgName("CLOSED")] Closed,
}

public enum TargetSplitDimension
{
    [PgName("ORG")] Org,
    [PgName("PERIOD")] Period,
}
