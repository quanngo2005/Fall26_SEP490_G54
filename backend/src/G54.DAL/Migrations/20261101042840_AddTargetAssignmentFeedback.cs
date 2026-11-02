using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddTargetAssignmentFeedback : Migration
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
            .Annotation("Npgsql:Enum:public.performance_evaluation_history_action", "CALCULATED,STATUS_CHANGED,APPROVED,DISPUTED,DISPUTE_WITHDRAWN,APPEAL_RESOLVED")
            .Annotation("Npgsql:Enum:public.performance_evaluation_status", "DRAFT,CALCULATED,IN_REVIEW,APPROVED,DISPUTED,FINAL")
            .Annotation("Npgsql:Enum:public.period_status", "OPEN,IN_REVIEW,LOCKED,CLOSED")
            .Annotation("Npgsql:Enum:public.period_type", "YEAR,HALF_YEAR,QUARTER,MONTH,WEEK,DAY")
            .Annotation("Npgsql:Enum:public.record_status", "ACTIVE,INACTIVE,ARCHIVED")
            .Annotation("Npgsql:Enum:public.risk_event_severity", "LOW,MEDIUM,HIGH,CRITICAL")
            .Annotation("Npgsql:Enum:public.risk_event_status", "OPEN,CONFIRMED,DISMISSED,RESOLVED")
            .Annotation("Npgsql:Enum:public.submission_status", "DRAFT,SUBMITTED,UNDER_REVIEW,APPROVED,REJECTED,WITHDRAWN")
            .Annotation("Npgsql:Enum:public.target_assignment_feedback_type", "CONFIRM,COMMENT")
            .Annotation("Npgsql:Enum:public.target_assignment_status", "DRAFT,ACTIVE,SUPERSEDED,CLOSED")
            .Annotation("Npgsql:Enum:public.target_split_dimension", "ORG,PERIOD")
            .Annotation("Npgsql:Enum:public.verification_decision", "PENDING,APPROVED,REJECTED,NEED_MORE_INFO")
            .Annotation("Npgsql:Enum:public.verified_result_status", "VALID,INVALID")
            .Annotation("Npgsql:Enum:public.work_priority", "LOW,MEDIUM,HIGH,CRITICAL")
            .Annotation("Npgsql:Enum:public.work_status", "NEW,IN_PROGRESS,HANDED_OVER,SUBMITTED,VERIFIED,REJECTED,DONE,CANCELLED,OVERDUE")
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
            .OldAnnotation("Npgsql:Enum:public.performance_evaluation_history_action", "CALCULATED,STATUS_CHANGED,APPROVED,DISPUTED,DISPUTE_WITHDRAWN,APPEAL_RESOLVED")
            .OldAnnotation("Npgsql:Enum:public.performance_evaluation_status", "DRAFT,CALCULATED,IN_REVIEW,APPROVED,DISPUTED,FINAL")
            .OldAnnotation("Npgsql:Enum:public.period_status", "OPEN,IN_REVIEW,LOCKED,CLOSED")
            .OldAnnotation("Npgsql:Enum:public.period_type", "YEAR,HALF_YEAR,QUARTER,MONTH,WEEK,DAY")
            .OldAnnotation("Npgsql:Enum:public.record_status", "ACTIVE,INACTIVE,ARCHIVED")
            .OldAnnotation("Npgsql:Enum:public.risk_event_severity", "LOW,MEDIUM,HIGH,CRITICAL")
            .OldAnnotation("Npgsql:Enum:public.risk_event_status", "OPEN,CONFIRMED,DISMISSED,RESOLVED")
            .OldAnnotation("Npgsql:Enum:public.submission_status", "DRAFT,SUBMITTED,UNDER_REVIEW,APPROVED,REJECTED,WITHDRAWN")
            .OldAnnotation("Npgsql:Enum:public.target_assignment_status", "DRAFT,ACTIVE,SUPERSEDED,CLOSED")
            .OldAnnotation("Npgsql:Enum:public.target_split_dimension", "ORG,PERIOD")
            .OldAnnotation("Npgsql:Enum:public.verification_decision", "PENDING,APPROVED,REJECTED,NEED_MORE_INFO")
            .OldAnnotation("Npgsql:Enum:public.verified_result_status", "VALID,INVALID")
            .OldAnnotation("Npgsql:Enum:public.work_priority", "LOW,MEDIUM,HIGH,CRITICAL")
            .OldAnnotation("Npgsql:Enum:public.work_status", "NEW,IN_PROGRESS,HANDED_OVER,SUBMITTED,VERIFIED,REJECTED,DONE,CANCELLED,OVERDUE")
            .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");

        migrationBuilder.CreateTable(
            name: "target_assignment_feedback",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                target_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<int>(type: "target_assignment_feedback_type", nullable: false),
                comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("target_assignment_feedback_pkey", x => x.id);
                table.CheckConstraint("ck_target_assignment_feedback_comment_len", "comment IS NULL OR char_length(comment) <= 500");
                table.ForeignKey(
                    name: "fk_target_assignment_feedback_employee",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_target_assignment_feedback_target",
                    column: x => x.target_assignment_id,
                    principalTable: "target_assignment",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_target_assignment_feedback_employee",
            table: "target_assignment_feedback",
            column: "employee_id");

        migrationBuilder.CreateIndex(
            name: "ix_target_assignment_feedback_target",
            table: "target_assignment_feedback",
            column: "target_assignment_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "target_assignment_feedback");

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
            .Annotation("Npgsql:Enum:public.performance_evaluation_history_action", "CALCULATED,STATUS_CHANGED,APPROVED,DISPUTED,DISPUTE_WITHDRAWN,APPEAL_RESOLVED")
            .Annotation("Npgsql:Enum:public.performance_evaluation_status", "DRAFT,CALCULATED,IN_REVIEW,APPROVED,DISPUTED,FINAL")
            .Annotation("Npgsql:Enum:public.period_status", "OPEN,IN_REVIEW,LOCKED,CLOSED")
            .Annotation("Npgsql:Enum:public.period_type", "YEAR,HALF_YEAR,QUARTER,MONTH,WEEK,DAY")
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
            .OldAnnotation("Npgsql:Enum:public.performance_evaluation_history_action", "CALCULATED,STATUS_CHANGED,APPROVED,DISPUTED,DISPUTE_WITHDRAWN,APPEAL_RESOLVED")
            .OldAnnotation("Npgsql:Enum:public.performance_evaluation_status", "DRAFT,CALCULATED,IN_REVIEW,APPROVED,DISPUTED,FINAL")
            .OldAnnotation("Npgsql:Enum:public.period_status", "OPEN,IN_REVIEW,LOCKED,CLOSED")
            .OldAnnotation("Npgsql:Enum:public.period_type", "YEAR,HALF_YEAR,QUARTER,MONTH,WEEK,DAY")
            .OldAnnotation("Npgsql:Enum:public.record_status", "ACTIVE,INACTIVE,ARCHIVED")
            .OldAnnotation("Npgsql:Enum:public.risk_event_severity", "LOW,MEDIUM,HIGH,CRITICAL")
            .OldAnnotation("Npgsql:Enum:public.risk_event_status", "OPEN,CONFIRMED,DISMISSED,RESOLVED")
            .OldAnnotation("Npgsql:Enum:public.submission_status", "DRAFT,SUBMITTED,UNDER_REVIEW,APPROVED,REJECTED,WITHDRAWN")
            .OldAnnotation("Npgsql:Enum:public.target_assignment_feedback_type", "CONFIRM,COMMENT")
            .OldAnnotation("Npgsql:Enum:public.target_assignment_status", "DRAFT,ACTIVE,SUPERSEDED,CLOSED")
            .OldAnnotation("Npgsql:Enum:public.target_split_dimension", "ORG,PERIOD")
            .OldAnnotation("Npgsql:Enum:public.verification_decision", "PENDING,APPROVED,REJECTED,NEED_MORE_INFO")
            .OldAnnotation("Npgsql:Enum:public.verified_result_status", "VALID,INVALID")
            .OldAnnotation("Npgsql:Enum:public.work_priority", "LOW,MEDIUM,HIGH,CRITICAL")
            .OldAnnotation("Npgsql:Enum:public.work_status", "NEW,IN_PROGRESS,HANDED_OVER,SUBMITTED,VERIFIED,REJECTED,DONE,CANCELLED,OVERDUE")
            .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");
    }
}
