using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddWorkStatusOverdue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:public.actual_progress_status", "VALID,INVALID")
            .Annotation("Npgsql:Enum:public.appeal_status", "OPEN,UNDER_REVIEW,ACCEPTED,REJECTED,WITHDRAWN")
            .Annotation("Npgsql:Enum:public.business_campaign_status", "PLANNED,RUNNING,PAUSED,COMPLETED,CANCELLED")
            .Annotation("Npgsql:Enum:public.business_objective_status", "DRAFT,ACTIVE,CLOSED,CANCELLED")
            .Annotation("Npgsql:Enum:public.department_level_type", "BRANCH,DEPARTMENT,TEAM")
            .Annotation("Npgsql:Enum:public.employee_status", "ACTIVE,INACTIVE,ON_LEAVE,TERMINATED")
            .Annotation("Npgsql:Enum:public.evaluation_profile_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED")
            .Annotation("Npgsql:Enum:public.handover_status", "PENDING,ACCEPTED,REJECTED,CANCELLED")
            .Annotation("Npgsql:Enum:public.kpi_aggregation_type", "SUM,RATIO,AVG,NO_AGGREGATION")
            .Annotation("Npgsql:Enum:public.kpi_direction", "HIGHER_BETTER,LOWER_BETTER,ON_TARGET")
            .Annotation("Npgsql:Enum:public.kpi_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED")
            .Annotation("Npgsql:Enum:public.performance_evaluation_status", "DRAFT,CALCULATED,IN_REVIEW,APPROVED,DISPUTED,FINAL")
            .Annotation("Npgsql:Enum:public.period_status", "OPEN,IN_REVIEW,LOCKED,CLOSED")
            .Annotation("Npgsql:Enum:public.period_type", "YEAR,HALF_YEAR,QUARTER,MONTH,DAY")
            .Annotation("Npgsql:Enum:public.record_status", "ACTIVE,INACTIVE,ARCHIVED")
            .Annotation("Npgsql:Enum:public.risk_event_severity", "LOW,MEDIUM,HIGH,CRITICAL")
            .Annotation("Npgsql:Enum:public.risk_event_status", "OPEN,CONFIRMED,DISMISSED,RESOLVED")
            .Annotation("Npgsql:Enum:public.submission_status", "DRAFT,SUBMITTED,UNDER_REVIEW,APPROVED,REJECTED,WITHDRAWN")
            .Annotation("Npgsql:Enum:public.target_assignment_status", "DRAFT,ACTIVE,SUPERSEDED,CLOSED")
            .Annotation("Npgsql:Enum:public.target_split_dimension", "ORG,PERIOD")
            .Annotation("Npgsql:Enum:public.verification_decision", "PENDING,APPROVED,REJECTED,NEED_MORE_INFO")
            .Annotation("Npgsql:Enum:public.verified_result_status", "VALID,INVALID")
            .Annotation("Npgsql:Enum:public.work_priority", "LOW,MEDIUM,HIGH,CRITICAL")
            .Annotation("Npgsql:Enum:public.work_status", "NEW,IN_PROGRESS,HANDED_OVER,SUBMITTED,VERIFIED,REJECTED,DONE,CANCELLED,OVERDUE")
            .Annotation("Npgsql:Enum:public.workflow_definition_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED")
            .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
            .OldAnnotation("Npgsql:Enum:public.actual_progress_status", "VALID,INVALID")
            .OldAnnotation("Npgsql:Enum:public.appeal_status", "OPEN,UNDER_REVIEW,ACCEPTED,REJECTED,WITHDRAWN")
            .OldAnnotation("Npgsql:Enum:public.business_campaign_status", "PLANNED,RUNNING,PAUSED,COMPLETED,CANCELLED")
            .OldAnnotation("Npgsql:Enum:public.business_objective_status", "DRAFT,ACTIVE,CLOSED,CANCELLED")
            .OldAnnotation("Npgsql:Enum:public.department_level_type", "BRANCH,DEPARTMENT,TEAM")
            .OldAnnotation("Npgsql:Enum:public.employee_status", "ACTIVE,INACTIVE,ON_LEAVE,TERMINATED")
            .OldAnnotation("Npgsql:Enum:public.evaluation_profile_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED")
            .OldAnnotation("Npgsql:Enum:public.handover_status", "PENDING,ACCEPTED,REJECTED,CANCELLED")
            .OldAnnotation("Npgsql:Enum:public.kpi_aggregation_type", "SUM,RATIO,AVG,NO_AGGREGATION")
            .OldAnnotation("Npgsql:Enum:public.kpi_direction", "HIGHER_BETTER,LOWER_BETTER,ON_TARGET")
            .OldAnnotation("Npgsql:Enum:public.kpi_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED")
            .OldAnnotation("Npgsql:Enum:public.performance_evaluation_status", "DRAFT,CALCULATED,IN_REVIEW,APPROVED,DISPUTED,FINAL")
            .OldAnnotation("Npgsql:Enum:public.period_status", "OPEN,IN_REVIEW,LOCKED,CLOSED")
            .OldAnnotation("Npgsql:Enum:public.period_type", "YEAR,HALF_YEAR,QUARTER,MONTH,DAY")
            .OldAnnotation("Npgsql:Enum:public.record_status", "ACTIVE,INACTIVE,ARCHIVED")
            .OldAnnotation("Npgsql:Enum:public.risk_event_severity", "LOW,MEDIUM,HIGH,CRITICAL")
            .OldAnnotation("Npgsql:Enum:public.risk_event_status", "OPEN,CONFIRMED,DISMISSED,RESOLVED")
            .OldAnnotation("Npgsql:Enum:public.submission_status", "DRAFT,SUBMITTED,UNDER_REVIEW,APPROVED,REJECTED,WITHDRAWN")
            .OldAnnotation("Npgsql:Enum:public.target_assignment_status", "DRAFT,ACTIVE,SUPERSEDED,CLOSED")
            .OldAnnotation("Npgsql:Enum:public.target_split_dimension", "ORG,PERIOD")
            .OldAnnotation("Npgsql:Enum:public.verification_decision", "PENDING,APPROVED,REJECTED,NEED_MORE_INFO")
            .OldAnnotation("Npgsql:Enum:public.verified_result_status", "VALID,INVALID")
            .OldAnnotation("Npgsql:Enum:public.work_priority", "LOW,MEDIUM,HIGH,CRITICAL")
            .OldAnnotation("Npgsql:Enum:public.work_status", "NEW,IN_PROGRESS,HANDED_OVER,SUBMITTED,VERIFIED,REJECTED,DONE,CANCELLED")
            .OldAnnotation("Npgsql:Enum:public.workflow_definition_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED")
            .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL cannot drop a single enum label, so the OVERDUE label stays in work_status.
        // Reverse the data change instead: reopen overdue work as IN_PROGRESS so no row keeps a status
        // the previous application version does not know about.
        migrationBuilder.Sql("UPDATE work SET status = 'IN_PROGRESS' WHERE status = 'OVERDUE';");
    }
}
