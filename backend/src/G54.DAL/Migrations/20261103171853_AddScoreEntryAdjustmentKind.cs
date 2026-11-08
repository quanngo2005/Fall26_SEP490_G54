using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddScoreEntryAdjustmentKind : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_score_entry_kind",
            table: "score_entry");

        migrationBuilder.DropCheckConstraint(
            name: "ck_score_entry_origin",
            table: "score_entry");

        migrationBuilder.AddCheckConstraint(
            name: "ck_score_entry_kind",
            table: "score_entry",
            sql: "(kind IN ('HOLD', 'TASK_DEDUCTION') AND points < 0 AND reverses_entry_id IS NULL) OR (kind = 'REFUND' AND points > 0 AND reverses_entry_id IS NOT NULL) OR (kind = 'ADJUSTMENT' AND points <> 0 AND reverses_entry_id IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_score_entry_origin",
            table: "score_entry",
            sql: "(kind = 'TASK_DEDUCTION' AND work_id IS NOT NULL AND appeal_id IS NULL) OR (kind IN ('HOLD', 'REFUND') AND appeal_id IS NOT NULL) OR (kind = 'ADJUSTMENT' AND appeal_id IS NOT NULL AND work_id IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // score_entry is append-only (trigger protect_score_entry), so ADJUSTMENT rows cannot be removed here.
        // Stop with a clear message instead of failing later on the restored check constraint.
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM score_entry WHERE kind = 'ADJUSTMENT') THEN
                    RAISE EXCEPTION 'Cannot roll back: score_entry already holds ADJUSTMENT rows, which are append-only.';
                END IF;
            END
            $$;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_score_entry_kind",
            table: "score_entry");

        migrationBuilder.DropCheckConstraint(
            name: "ck_score_entry_origin",
            table: "score_entry");

        migrationBuilder.AddCheckConstraint(
            name: "ck_score_entry_kind",
            table: "score_entry",
            sql: "(kind IN ('HOLD', 'TASK_DEDUCTION') AND points < 0 AND reverses_entry_id IS NULL) OR (kind = 'REFUND' AND points > 0 AND reverses_entry_id IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_score_entry_origin",
            table: "score_entry",
            sql: "(kind = 'TASK_DEDUCTION' AND work_id IS NOT NULL AND appeal_id IS NULL) OR (kind IN ('HOLD', 'REFUND') AND appeal_id IS NOT NULL)");
    }
}
