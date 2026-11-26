using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddEmployeeTargetAckAndChangeLog : Migration
{
    private static readonly string[] EmployeeYearColumns = { "employee_id", "year" };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:public.actual_progress_status", "VALID,INVALID")
            .Annotation("Npgsql:Enum:public.appeal_status", "OPEN,UNDER_REVIEW,ACCEPTED,REJECTED,WITHDRAWN")
            .Annotation("Npgsql:Enum:public.business_campaign_status", "PLANNED,RUNNING,PAUSED,COMPLETED,CANCELLED")
            .Annotation("Npgsql:Enum:public.business_objective_status", "DRAFT,ACTIVE,CLOSED,CANCELLED")
            .Annotation("Npgsql:Enum:public.department_level_type", "BRANCH,DEPARTMENT,TEAM")
            .Annotation("Npgsql:Enum:public.employee_status", "ACTIVE,INACTIVE,ON_LEAVE,TERMINATED,PENDING_RESIGNATION")
            .Annotation("Npgsql:Enum:public.evaluation_profile_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
            .Annotation("Npgsql:Enum:public.handover_status", "PENDING,ACCEPTED,REJECTED,CANCELLED")
            .Annotation("Npgsql:Enum:public.kpi_aggregation_type", "SUM,RATIO,AVG,NO_AGGREGATION,BALANCE")
            .Annotation("Npgsql:Enum:public.kpi_direction", "HIGHER_BETTER,LOWER_BETTER,ON_TARGET")
            .Annotation("Npgsql:Enum:public.kpi_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
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
            .OldAnnotation("Npgsql:Enum:public.employee_status", "ACTIVE,INACTIVE,ON_LEAVE,TERMINATED,PENDING_RESIGNATION")
            .OldAnnotation("Npgsql:Enum:public.evaluation_profile_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
            .OldAnnotation("Npgsql:Enum:public.handover_status", "PENDING,ACCEPTED,REJECTED,CANCELLED")
            .OldAnnotation("Npgsql:Enum:public.kpi_aggregation_type", "SUM,RATIO,AVG,NO_AGGREGATION")
            .OldAnnotation("Npgsql:Enum:public.kpi_direction", "HIGHER_BETTER,LOWER_BETTER,ON_TARGET")
            .OldAnnotation("Npgsql:Enum:public.kpi_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
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

        migrationBuilder.AddColumn<decimal>(
            name: "proposed_q1",
            table: "target_assignment_feedback",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "proposed_q2",
            table: "target_assignment_feedback",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "proposed_q3",
            table: "target_assignment_feedback",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "proposed_q4",
            table: "target_assignment_feedback",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "proposed_year",
            table: "target_assignment_feedback",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "replied_at",
            table: "target_assignment_feedback",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "replied_by",
            table: "target_assignment_feedback",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "reply",
            table: "target_assignment_feedback",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "employee_target_ack",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("employee_target_ack_pkey", x => x.id);
                table.CheckConstraint("ck_employee_target_ack_status", "status IN ('PENDING', 'CONFIRMED', 'RECONFIRM')");
                table.ForeignKey(
                    name: "fk_employee_target_ack_employee",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "target_change_log",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                tier = table.Column<int>(type: "integer", nullable: false),
                kpi_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                old_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                new_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                rounding_step = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                department_id = table.Column<Guid>(type: "uuid", nullable: true),
                year = table.Column<int>(type: "integer", nullable: true),
                scope_json = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("target_change_log_pkey", x => x.id);
                table.CheckConstraint("ck_target_change_log_action", "action IN ('SUGGEST', 'MANUAL', 'PUBLISH', 'ADJUST', 'FEEDBACK', 'REPLY')");
                table.CheckConstraint("ck_target_change_log_tier", "tier IN (1, 2, 3)");
                table.ForeignKey(
                    name: "fk_target_change_log_department",
                    column: x => x.department_id,
                    principalTable: "department",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_target_change_log_employee",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_target_change_log_kpi",
                    column: x => x.kpi_definition_id,
                    principalTable: "kpi_definition",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_target_assignment_feedback_replied_by",
            table: "target_assignment_feedback",
            column: "replied_by");

        migrationBuilder.AddCheckConstraint(
            name: "ck_target_assignment_feedback_replied_by_time",
            table: "target_assignment_feedback",
            sql: "(replied_by IS NULL) = (replied_at IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_target_assignment_feedback_reply_len",
            table: "target_assignment_feedback",
            sql: "reply IS NULL OR char_length(reply) <= 500");

        migrationBuilder.CreateIndex(
            name: "uq_employee_target_ack_employee_year",
            table: "employee_target_ack",
            columns: EmployeeYearColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_target_change_log_department_id",
            table: "target_change_log",
            column: "department_id");

        migrationBuilder.CreateIndex(
            name: "ix_target_change_log_employee_year",
            table: "target_change_log",
            columns: EmployeeYearColumns);

        migrationBuilder.CreateIndex(
            name: "ix_target_change_log_kpi",
            table: "target_change_log",
            column: "kpi_definition_id");

        migrationBuilder.AddForeignKey(
            name: "fk_target_assignment_feedback_replied_by",
            table: "target_assignment_feedback",
            column: "replied_by",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Postgres cannot drop an enum label (ALTER TYPE ... DROP VALUE does not exist), so the automatic
        // AlterDatabase diff below intentionally keeps BALANCE in both Annotation and OldAnnotation for
        // kpi_aggregation_type (no diff => no generated ADD/DROP VALUE for it). Instead, remap any BALANCE rows
        // to SUM and recreate the type without BALANCE using the standard rename/create/cast/drop pattern.
        migrationBuilder.Sql("""
            UPDATE kpi_definition SET aggregation_type = 'SUM' WHERE aggregation_type = 'BALANCE';
            ALTER TYPE kpi_aggregation_type RENAME TO kpi_aggregation_type_old;
            CREATE TYPE kpi_aggregation_type AS ENUM ('SUM', 'RATIO', 'AVG', 'NO_AGGREGATION');
            ALTER TABLE kpi_definition ALTER COLUMN aggregation_type TYPE kpi_aggregation_type USING aggregation_type::text::kpi_aggregation_type;
            DROP TYPE kpi_aggregation_type_old;
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_target_assignment_feedback_replied_by",
            table: "target_assignment_feedback");

        migrationBuilder.DropTable(
            name: "employee_target_ack");

        migrationBuilder.DropTable(
            name: "target_change_log");

        migrationBuilder.DropIndex(
            name: "IX_target_assignment_feedback_replied_by",
            table: "target_assignment_feedback");

        migrationBuilder.DropCheckConstraint(
            name: "ck_target_assignment_feedback_replied_by_time",
            table: "target_assignment_feedback");

        migrationBuilder.DropCheckConstraint(
            name: "ck_target_assignment_feedback_reply_len",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "proposed_q1",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "proposed_q2",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "proposed_q3",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "proposed_q4",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "proposed_year",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "replied_at",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "replied_by",
            table: "target_assignment_feedback");

        migrationBuilder.DropColumn(
            name: "reply",
            table: "target_assignment_feedback");

        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:public.actual_progress_status", "VALID,INVALID")
            .Annotation("Npgsql:Enum:public.appeal_status", "OPEN,UNDER_REVIEW,ACCEPTED,REJECTED,WITHDRAWN")
            .Annotation("Npgsql:Enum:public.business_campaign_status", "PLANNED,RUNNING,PAUSED,COMPLETED,CANCELLED")
            .Annotation("Npgsql:Enum:public.business_objective_status", "DRAFT,ACTIVE,CLOSED,CANCELLED")
            .Annotation("Npgsql:Enum:public.department_level_type", "BRANCH,DEPARTMENT,TEAM")
            .Annotation("Npgsql:Enum:public.employee_status", "ACTIVE,INACTIVE,ON_LEAVE,TERMINATED,PENDING_RESIGNATION")
            .Annotation("Npgsql:Enum:public.evaluation_profile_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
            .Annotation("Npgsql:Enum:public.handover_status", "PENDING,ACCEPTED,REJECTED,CANCELLED")
            .Annotation("Npgsql:Enum:public.kpi_aggregation_type", "SUM,RATIO,AVG,NO_AGGREGATION,BALANCE")
            .Annotation("Npgsql:Enum:public.kpi_direction", "HIGHER_BETTER,LOWER_BETTER,ON_TARGET")
            .Annotation("Npgsql:Enum:public.kpi_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
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
            .OldAnnotation("Npgsql:Enum:public.employee_status", "ACTIVE,INACTIVE,ON_LEAVE,TERMINATED,PENDING_RESIGNATION")
            .OldAnnotation("Npgsql:Enum:public.evaluation_profile_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
            .OldAnnotation("Npgsql:Enum:public.handover_status", "PENDING,ACCEPTED,REJECTED,CANCELLED")
            .OldAnnotation("Npgsql:Enum:public.kpi_aggregation_type", "SUM,RATIO,AVG,NO_AGGREGATION,BALANCE")
            .OldAnnotation("Npgsql:Enum:public.kpi_direction", "HIGHER_BETTER,LOWER_BETTER,ON_TARGET")
            .OldAnnotation("Npgsql:Enum:public.kpi_status", "DRAFT,ACTIVE,INACTIVE,ARCHIVED,PENDING_APPROVAL,REJECTED")
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
