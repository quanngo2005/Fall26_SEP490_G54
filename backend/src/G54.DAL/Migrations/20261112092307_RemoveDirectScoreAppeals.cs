using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class RemoveDirectScoreAppeals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The score ledger goes first: its rows reference the appeals removed below.
        migrationBuilder.DropTable(
            name: "score_entry");

        migrationBuilder.Sql("DROP FUNCTION protect_score_entry();");

        migrationBuilder.DropTable(
            name: "appeal_score_setting");

        // Score appeals no longer exist: give back the score they froze (unless a result appeal still does), then remove them.
        migrationBuilder.Sql("""
            UPDATE performance_evaluation pe
            SET status = COALESCE(pe.pre_dispute_status, 'CALCULATED'::performance_evaluation_status),
                pre_dispute_status = NULL,
                disputed_at = NULL
            WHERE pe.status = 'DISPUTED'::performance_evaluation_status
              AND pe.id IN (SELECT performance_evaluation_id FROM appeal WHERE performance_evaluation_id IS NOT NULL)
              AND NOT EXISTS (SELECT 1 FROM appeal r
                              WHERE r.employee_id = pe.employee_id
                                AND r.result_submission_id IS NOT NULL
                                AND r.status::text IN ('OPEN', 'UNDER_REVIEW'));
            DELETE FROM evidence
            WHERE appeal_id IN (SELECT id FROM appeal WHERE result_submission_id IS NULL);
            DELETE FROM appeal WHERE result_submission_id IS NULL;
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_appeal_evaluation",
            table: "appeal");

        migrationBuilder.DropIndex(
            name: "ix_appeal_evaluation",
            table: "appeal");

        migrationBuilder.DropCheckConstraint(
            name: "ck_appeal_target_xor",
            table: "appeal");

        migrationBuilder.DropColumn(
            name: "performance_evaluation_id",
            table: "appeal");

        migrationBuilder.DropColumn(
            name: "score_dispute_reason",
            table: "appeal");

        migrationBuilder.AlterColumn<Guid>(
            name: "result_submission_id",
            table: "appeal",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "result_submission_id",
            table: "appeal",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<Guid>(
            name: "performance_evaluation_id",
            table: "appeal",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "score_dispute_reason",
            table: "appeal",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "appeal_score_setting",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                default_points = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_appeal_score_setting", x => x.id);
                table.CheckConstraint("ck_appeal_score_setting_points", "default_points > 0 AND default_points <= 1000");
                table.CheckConstraint("ck_appeal_score_setting_singleton", "id = 1");
            });

        migrationBuilder.CreateTable(
            name: "score_entry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                appeal_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                points = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                recorded_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                work_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_score_entry", x => x.id);
                table.CheckConstraint("ck_score_entry_kind", "(kind IN ('HOLD', 'TASK_DEDUCTION') AND points < 0 AND reverses_entry_id IS NULL) OR (kind = 'REFUND' AND points > 0 AND reverses_entry_id IS NOT NULL) OR (kind = 'ADJUSTMENT' AND points <> 0 AND reverses_entry_id IS NULL)");
                table.CheckConstraint("ck_score_entry_origin", "(kind = 'TASK_DEDUCTION' AND work_id IS NOT NULL AND appeal_id IS NULL) OR (kind IN ('HOLD', 'REFUND') AND appeal_id IS NOT NULL) OR (kind = 'ADJUSTMENT' AND appeal_id IS NOT NULL AND work_id IS NULL)");
                table.ForeignKey(
                    name: "FK_score_entry_appeal_appeal_id",
                    column: x => x.appeal_id,
                    principalTable: "appeal",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_score_entry_employee_employee_id",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_score_entry_employee_recorded_by_employee_id",
                    column: x => x.recorded_by_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_score_entry_performance_evaluation_evaluation_id",
                    column: x => x.evaluation_id,
                    principalTable: "performance_evaluation",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_score_entry_score_entry_reverses_entry_id",
                    column: x => x.reverses_entry_id,
                    principalTable: "score_entry",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_score_entry_work_work_id",
                    column: x => x.work_id,
                    principalTable: "work",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        // Raw SQL: the entity of this table no longer exists, so InsertData has no model to read.
        migrationBuilder.Sql("INSERT INTO appeal_score_setting (id, default_points) VALUES (1, 5);");

        migrationBuilder.CreateIndex(
            name: "ix_appeal_evaluation",
            table: "appeal",
            column: "performance_evaluation_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_appeal_target_xor",
            table: "appeal",
            sql: "(result_submission_id IS NULL) <> (performance_evaluation_id IS NULL)");

        migrationBuilder.CreateIndex(
            name: "IX_score_entry_appeal_id_kind",
            table: "score_entry",
            columns: ["appeal_id", "kind"],
            unique: true,
            filter: "appeal_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_score_entry_employee_id_kind",
            table: "score_entry",
            columns: ["employee_id", "kind"]);

        migrationBuilder.CreateIndex(
            name: "IX_score_entry_evaluation_id",
            table: "score_entry",
            column: "evaluation_id");

        migrationBuilder.CreateIndex(
            name: "IX_score_entry_recorded_by_employee_id",
            table: "score_entry",
            column: "recorded_by_employee_id");

        migrationBuilder.CreateIndex(
            name: "IX_score_entry_reverses_entry_id",
            table: "score_entry",
            column: "reverses_entry_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_score_entry_work_id",
            table: "score_entry",
            column: "work_id");

        migrationBuilder.AddForeignKey(
            name: "fk_appeal_evaluation",
            table: "appeal",
            column: "performance_evaluation_id",
            principalTable: "performance_evaluation",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            CREATE FUNCTION protect_score_entry() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE original score_entry;
            BEGIN
                IF TG_OP <> 'INSERT' THEN
                    RAISE EXCEPTION 'Score entries are append-only';
                END IF;
                IF NEW.kind = 'REFUND' THEN
                    SELECT * INTO original FROM score_entry WHERE id = NEW.reverses_entry_id;
                    IF NOT FOUND OR original.kind <> 'HOLD'
                        OR NEW.appeal_id IS DISTINCT FROM original.appeal_id
                        OR NEW.evaluation_id <> original.evaluation_id
                        OR NEW.employee_id <> original.employee_id OR NEW.points <> -original.points THEN
                        RAISE EXCEPTION 'Refund must exactly reverse its original hold';
                    END IF;
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER protect_score_entry BEFORE INSERT OR UPDATE OR DELETE ON score_entry
                FOR EACH ROW EXECUTE FUNCTION protect_score_entry();
            """);
    }
}
