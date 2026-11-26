using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddCampaignCustomer : Migration
{
    private static readonly string[] AssigneeIndexColumns = { "business_campaign_id", "assignee_employee_id" };
    private static readonly string[] CifIndexColumns = { "business_campaign_id", "customer_cif" };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:public.actual_progress_status", "VALID,INVALID")
            .Annotation("Npgsql:Enum:public.appeal_status", "OPEN,UNDER_REVIEW,ACCEPTED,REJECTED,WITHDRAWN")
            .Annotation("Npgsql:Enum:public.business_campaign_status", "PLANNED,RUNNING,PAUSED,COMPLETED,CANCELLED")
            .Annotation("Npgsql:Enum:public.business_objective_status", "DRAFT,ACTIVE,CLOSED,CANCELLED")
            .Annotation("Npgsql:Enum:public.campaign_contact_status", "NOT_CONTACTED,CALLED,APPOINTMENT_SET,MET,CLOSED")
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

        // Note: this migration does not touch "notification"."type"/"related_type" column lengths, even though
        // the model (AppDbContext.Notifications.cs, GB-914) now declares 40/30 while the last migration shipped
        // 30/20 — that drift predates this change and is out of scope for CampaignCustomer (Phase 1); it needs
        // its own migration.

        migrationBuilder.CreateTable(
            name: "campaign_customer",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                business_campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_cif = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                assignee_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                contact_status = table.Column<int>(type: "campaign_contact_status", nullable: false, defaultValueSql: "'NOT_CONTACTED'::campaign_contact_status"),
                call_answered = table.Column<bool>(type: "boolean", nullable: true),
                missed_call_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                appointment_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                met_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                closed_work_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                work_id = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("campaign_customer_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_campaign_customer_assignee",
                    column: x => x.assignee_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_campaign_customer_campaign",
                    column: x => x.business_campaign_id,
                    principalTable: "business_campaign",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_campaign_customer_closed_work_type",
                    column: x => x.closed_work_type_id,
                    principalTable: "work_type",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_campaign_customer_work",
                    column: x => x.work_id,
                    principalTable: "work",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_campaign_customer_assignee",
            table: "campaign_customer",
            columns: AssigneeIndexColumns);

        migrationBuilder.CreateIndex(
            name: "IX_campaign_customer_assignee_employee_id",
            table: "campaign_customer",
            column: "assignee_employee_id");

        migrationBuilder.CreateIndex(
            name: "IX_campaign_customer_closed_work_type_id",
            table: "campaign_customer",
            column: "closed_work_type_id");

        migrationBuilder.CreateIndex(
            name: "IX_campaign_customer_work_id",
            table: "campaign_customer",
            column: "work_id");

        migrationBuilder.CreateIndex(
            name: "uq_campaign_customer_cif",
            table: "campaign_customer",
            columns: CifIndexColumns,
            unique: true);

        // Backfills the CAMPAIGN_CONTACT permission for existing databases; fresh installs also get it from
        // seed_dev_data.sql. Idempotent and data-only, same idiom as GrantEvaluationCalculate.
        migrationBuilder.Sql("""
            INSERT INTO permission (id, code, name, resource, action)
            VALUES ('5eed0000-0000-0000-0009-000000000037', 'CAMPAIGN_CONTACT', 'Cập nhật liên hệ khách hàng chiến dịch', 'campaign_customer', 'update')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'Own'
            FROM role r
            JOIN permission p ON p.code = 'CAMPAIGN_CONTACT'
            WHERE r.code IN ('EMPLOYEE', 'MANAGER', 'DEPT_MANAGER', 'BRANCH_MANAGER')
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'CAMPAIGN_CONTACT');
            DELETE FROM permission WHERE code = 'CAMPAIGN_CONTACT';
            """);

        migrationBuilder.DropTable(
            name: "campaign_customer");

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
            .OldAnnotation("Npgsql:Enum:public.campaign_contact_status", "NOT_CONTACTED,CALLED,APPOINTMENT_SET,MET,CLOSED")
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
