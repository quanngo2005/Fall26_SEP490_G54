using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddAppealScoreLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                appeal_id = table.Column<Guid>(type: "uuid", nullable: true),
                work_id = table.Column<Guid>(type: "uuid", nullable: true),
                reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                recorded_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                points = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_score_entry", x => x.id);
                table.CheckConstraint("ck_score_entry_kind", "(kind IN ('HOLD', 'TASK_DEDUCTION') AND points < 0 AND reverses_entry_id IS NULL) OR (kind = 'REFUND' AND points > 0 AND reverses_entry_id IS NOT NULL)");
                table.CheckConstraint("ck_score_entry_origin", "(kind = 'TASK_DEDUCTION' AND work_id IS NOT NULL AND appeal_id IS NULL) OR (kind IN ('HOLD', 'REFUND') AND appeal_id IS NOT NULL)");
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

        migrationBuilder.InsertData(
            table: "appeal_score_setting",
            columns: ["id", "default_points"],
            values: new object[] { 1, 5m });

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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER protect_score_entry ON score_entry; DROP FUNCTION protect_score_entry();");
        migrationBuilder.DropTable(
            name: "appeal_score_setting");

        migrationBuilder.DropTable(
            name: "score_entry");

        migrationBuilder.DropColumn(
            name: "score_dispute_reason",
            table: "appeal");
    }
}
