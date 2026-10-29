using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations
{
    /// <inheritdoc />
    public partial class RestructureEvaluationProfileItemTree : Migration
    {
        private static readonly string[] ProfileKpiColumns = ["evaluation_profile_id", "kpi_definition_id"];
        private static readonly string[] HistoryIndexColumns = ["performance_evaluation_id", "created_at"];
        private static readonly string[] ProfileGroupColumns = ["evaluation_profile_id", "kpi_group_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Group-based items cannot be mapped onto the KPI tree; they are discarded and reseeded.
            // Profiles left without items go back to Draft so they cannot be used for scoring until reconfigured.
            // The weight-sum constraint triggers are deferred; firing them now leaves no pending trigger events
            // that would block the ALTER TABLE statements below. Profiles leave ACTIVE first so the delete passes.
            migrationBuilder.Sql("SET CONSTRAINTS ALL IMMEDIATE;");
            migrationBuilder.Sql("UPDATE evaluation_profile SET status = 'DRAFT' WHERE status = 'ACTIVE';");
            migrationBuilder.Sql("DELETE FROM evaluation_profile_item;");

            // The ERD script created the unique key as a constraint, while the EF model sees an index;
            // drop whichever exists so the migration runs on both schema origins.
            migrationBuilder.Sql("""
                ALTER TABLE evaluation_profile_item DROP CONSTRAINT IF EXISTS fk_evaluation_profile_item_group;
                ALTER TABLE evaluation_profile_item DROP CONSTRAINT IF EXISTS uq_evaluation_profile_item;
                DROP INDEX IF EXISTS uq_evaluation_profile_item;
                DROP INDEX IF EXISTS "IX_evaluation_profile_item_kpi_group_id";
                ALTER TABLE work_result_item DROP CONSTRAINT IF EXISTS ck_work_result_item_task_weight;
                """);

            migrationBuilder.DropColumn(
                name: "task_weight",
                table: "work_result_item");

            migrationBuilder.DropColumn(
                name: "kpi_group_id",
                table: "evaluation_profile_item");

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

            migrationBuilder.AddColumn<Guid>(
                name: "kpi_definition_id",
                table: "evaluation_profile_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "evaluation_profile_item",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_item_id",
                table: "evaluation_profile_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "performance_evaluation_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    performance_evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "performance_evaluation_history_action", nullable: false),
                    from_status = table.Column<int>(type: "performance_evaluation_status", nullable: true),
                    to_status = table.Column<int>(type: "performance_evaluation_status", nullable: false),
                    old_total_score = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    new_total_score = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("performance_evaluation_history_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fk_performance_evaluation_history_evaluation",
                        column: x => x.performance_evaluation_id,
                        principalTable: "performance_evaluation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_profile_item_kpi_definition",
                table: "evaluation_profile_item",
                column: "kpi_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_profile_item_parent",
                table: "evaluation_profile_item",
                column: "parent_item_id");

            migrationBuilder.CreateIndex(
                name: "uq_evaluation_profile_item_kpi",
                table: "evaluation_profile_item",
                columns: ProfileKpiColumns,
                unique: true,
                filter: "kpi_definition_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evaluation_profile_item_node",
                table: "evaluation_profile_item",
                sql: "num_nonnulls(name, kpi_definition_id) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_performance_evaluation_history_evaluation",
                table: "performance_evaluation_history",
                columns: HistoryIndexColumns);

            migrationBuilder.AddForeignKey(
                name: "fk_evaluation_profile_item_kpi_definition",
                table: "evaluation_profile_item",
                column: "kpi_definition_id",
                principalTable: "kpi_definition",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_evaluation_profile_item_parent",
                table: "evaluation_profile_item",
                column: "parent_item_id",
                principalTable: "evaluation_profile_item",
                principalColumn: "id");

            // Weight rules move from "all items total 100" to the tree: siblings under each parent (and the roots)
            // total 100, group nodes have children, and KPI leaves have none.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION evaluation_profile_tree_error(p_profile_id UUID)
                RETURNS TEXT LANGUAGE plpgsql AS $$
                DECLARE
                    bad_parent UUID;
                    bad_total NUMERIC(12,4);
                    bad_item UUID;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM evaluation_profile_item
                                   WHERE evaluation_profile_id = p_profile_id AND parent_item_id IS NULL) THEN
                        RETURN 'profile has no items';
                    END IF;

                    SELECT parent_item_id, SUM(weight_percent) INTO bad_parent, bad_total
                    FROM evaluation_profile_item
                    WHERE evaluation_profile_id = p_profile_id
                    GROUP BY parent_item_id
                    HAVING SUM(weight_percent) <> 100
                    LIMIT 1;
                    IF FOUND THEN
                        RETURN format('weights under %s total %s, expected 100', COALESCE(bad_parent::TEXT, 'root'), bad_total);
                    END IF;

                    SELECT g.id INTO bad_item
                    FROM evaluation_profile_item g
                    WHERE g.evaluation_profile_id = p_profile_id
                      AND g.kpi_definition_id IS NULL
                      AND NOT EXISTS (SELECT 1 FROM evaluation_profile_item c WHERE c.parent_item_id = g.id)
                    LIMIT 1;
                    IF FOUND THEN
                        RETURN format('group item %s has no children', bad_item);
                    END IF;

                    SELECT c.id INTO bad_item
                    FROM evaluation_profile_item c
                    JOIN evaluation_profile_item p ON p.id = c.parent_item_id
                    WHERE c.evaluation_profile_id = p_profile_id
                      AND (p.kpi_definition_id IS NOT NULL OR p.evaluation_profile_id <> c.evaluation_profile_id)
                    LIMIT 1;
                    IF FOUND THEN
                        RETURN format('item %s has a KPI leaf or another profile''s item as parent', bad_item);
                    END IF;

                    RETURN NULL;
                END;
                $$;

                CREATE OR REPLACE FUNCTION validate_profile_weight_sum()
                RETURNS TRIGGER LANGUAGE plpgsql AS $$
                DECLARE
                    profile_id UUID;
                    p_status evaluation_profile_status;
                    tree_error TEXT;
                BEGIN
                    profile_id := COALESCE(NEW.evaluation_profile_id, OLD.evaluation_profile_id);

                    SELECT status INTO p_status FROM evaluation_profile WHERE id = profile_id;

                    IF p_status = 'ACTIVE' THEN
                        tree_error := evaluation_profile_tree_error(profile_id);
                        IF tree_error IS NOT NULL THEN
                            RAISE EXCEPTION 'Active evaluation profile % has an invalid item tree: %', profile_id, tree_error;
                        END IF;
                    END IF;

                    RETURN NULL;
                END;
                $$;

                CREATE OR REPLACE FUNCTION validate_profile_activation()
                RETURNS TRIGGER LANGUAGE plpgsql AS $$
                DECLARE
                    tree_error TEXT;
                BEGIN
                    IF NEW.status = 'ACTIVE' THEN
                        tree_error := evaluation_profile_tree_error(NEW.id);
                        IF tree_error IS NOT NULL THEN
                            RAISE EXCEPTION 'Cannot activate evaluation profile %: %', NEW.id, tree_error;
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tree items have no KPI group to restore; they are discarded so the NOT NULL group FK can be recreated.
            migrationBuilder.Sql("SET CONSTRAINTS ALL IMMEDIATE;");
            migrationBuilder.Sql("UPDATE evaluation_profile SET status = 'DRAFT' WHERE status = 'ACTIVE';");
            migrationBuilder.Sql("DELETE FROM evaluation_profile_item;");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION validate_profile_weight_sum()
                RETURNS TRIGGER LANGUAGE plpgsql AS $$
                DECLARE
                    profile_id UUID;
                    total_weight NUMERIC(12,4);
                    p_status evaluation_profile_status;
                BEGIN
                    profile_id := COALESCE(NEW.evaluation_profile_id, OLD.evaluation_profile_id);

                    SELECT status INTO p_status FROM evaluation_profile WHERE id = profile_id;

                    IF p_status = 'ACTIVE' THEN
                        SELECT COALESCE(SUM(weight_percent), 0) INTO total_weight
                        FROM evaluation_profile_item
                        WHERE evaluation_profile_id = profile_id;

                        IF total_weight <> 100 THEN
                            RAISE EXCEPTION 'Active evaluation profile % must have total item weight = 100, got %',
                                profile_id, total_weight;
                        END IF;
                    END IF;

                    RETURN NULL;
                END;
                $$;

                CREATE OR REPLACE FUNCTION validate_profile_activation()
                RETURNS TRIGGER LANGUAGE plpgsql AS $$
                DECLARE
                    total_weight NUMERIC(12,4);
                BEGIN
                    IF NEW.status = 'ACTIVE' THEN
                        SELECT COALESCE(SUM(weight_percent), 0) INTO total_weight
                        FROM evaluation_profile_item
                        WHERE evaluation_profile_id = NEW.id;

                        IF total_weight <> 100 THEN
                            RAISE EXCEPTION 'Cannot activate evaluation profile %: item weights must total 100, got %',
                                NEW.id, total_weight;
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                DROP FUNCTION IF EXISTS evaluation_profile_tree_error(UUID);
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_evaluation_profile_item_kpi_definition",
                table: "evaluation_profile_item");

            migrationBuilder.DropForeignKey(
                name: "fk_evaluation_profile_item_parent",
                table: "evaluation_profile_item");

            migrationBuilder.DropTable(
                name: "performance_evaluation_history");

            migrationBuilder.DropIndex(
                name: "ix_evaluation_profile_item_kpi_definition",
                table: "evaluation_profile_item");

            migrationBuilder.DropIndex(
                name: "ix_evaluation_profile_item_parent",
                table: "evaluation_profile_item");

            migrationBuilder.DropIndex(
                name: "uq_evaluation_profile_item_kpi",
                table: "evaluation_profile_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evaluation_profile_item_node",
                table: "evaluation_profile_item");

            migrationBuilder.DropColumn(
                name: "kpi_definition_id",
                table: "evaluation_profile_item");

            migrationBuilder.DropColumn(
                name: "name",
                table: "evaluation_profile_item");

            migrationBuilder.DropColumn(
                name: "parent_item_id",
                table: "evaluation_profile_item");

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
                .OldAnnotation("Npgsql:Enum:public.target_assignment_status", "DRAFT,ACTIVE,SUPERSEDED,CLOSED")
                .OldAnnotation("Npgsql:Enum:public.target_split_dimension", "ORG,PERIOD")
                .OldAnnotation("Npgsql:Enum:public.verification_decision", "PENDING,APPROVED,REJECTED,NEED_MORE_INFO")
                .OldAnnotation("Npgsql:Enum:public.verified_result_status", "VALID,INVALID")
                .OldAnnotation("Npgsql:Enum:public.work_priority", "LOW,MEDIUM,HIGH,CRITICAL")
                .OldAnnotation("Npgsql:Enum:public.work_status", "NEW,IN_PROGRESS,HANDED_OVER,SUBMITTED,VERIFIED,REJECTED,DONE,CANCELLED,OVERDUE")
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.AddColumn<decimal>(
                name: "task_weight",
                table: "work_result_item",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_result_item_task_weight",
                table: "work_result_item",
                sql: "task_weight > 0");

            migrationBuilder.AddColumn<Guid>(
                name: "kpi_group_id",
                table: "evaluation_profile_item",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_profile_item_kpi_group_id",
                table: "evaluation_profile_item",
                column: "kpi_group_id");

            migrationBuilder.CreateIndex(
                name: "uq_evaluation_profile_item",
                table: "evaluation_profile_item",
                columns: ProfileGroupColumns,
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_evaluation_profile_item_group",
                table: "evaluation_profile_item",
                column: "kpi_group_id",
                principalTable: "kpi_group",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
