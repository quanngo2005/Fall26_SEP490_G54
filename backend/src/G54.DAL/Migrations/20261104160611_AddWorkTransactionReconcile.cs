using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddWorkTransactionReconcile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "value_source",
            table: "work_type_kpi",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "COUNT");

        migrationBuilder.AlterColumn<Guid>(
            name: "verification_id",
            table: "verified_work_result",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<Guid>(
            name: "work_transaction_id",
            table: "verified_work_result",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "work_transaction",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                transaction_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                imported_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("work_transaction_pkey", x => x.id);
                table.CheckConstraint("ck_work_transaction_status", "status IN ('IN_REVIEW','MATCHED','NOT_MATCHED')");
                table.ForeignKey(
                    name: "fk_work_transaction_imported",
                    column: x => x.imported_transaction_id,
                    principalTable: "imported_transaction",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_work_transaction_work",
                    column: x => x.work_id,
                    principalTable: "work",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "uq_verified_work_result_work_transaction",
            table: "verified_work_result",
            column: "work_transaction_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_work_transaction_work",
            table: "work_transaction",
            column: "work_id");

        migrationBuilder.CreateIndex(
            name: "uq_work_transaction_code",
            table: "work_transaction",
            column: "transaction_code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_work_transaction_imported",
            table: "work_transaction",
            column: "imported_transaction_id",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "fk_verified_work_result_work_transaction",
            table: "verified_work_result",
            column: "work_transaction_id",
            principalTable: "work_transaction",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION validate_work_result_item() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE
                v_work_type_id UUID;
                v_kpi UUID;
                v_agg kpi_aggregation_type;
                v_date DATE;
            BEGIN
                SELECT t.kpi_definition_id, k.aggregation_type, ap.progress_date
                INTO v_kpi, v_agg, v_date
                FROM actual_progress ap
                JOIN target_assignment t ON t.id = ap.target_assignment_id
                JOIN kpi_definition k ON k.id = t.kpi_definition_id
                WHERE ap.id = NEW.actual_progress_id;

                -- A result comes from a verification (manual) or from a matched work transaction (import).
                SELECT w.work_type_id, COALESCE(vwr.verified_at::date, v_date)
                INTO v_work_type_id, v_date
                FROM verified_work_result vwr
                LEFT JOIN verification v ON v.id = vwr.verification_id
                LEFT JOIN result_submission rs ON rs.id = v.result_submission_id
                LEFT JOIN work_transaction wt ON wt.id = vwr.work_transaction_id
                JOIN work w ON w.id = COALESCE(rs.work_id, wt.work_id)
                WHERE vwr.id = NEW.verified_work_result_id;

                IF v_work_type_id IS NULL THEN
                    RAISE EXCEPTION 'Unclassified Work cannot generate WorkResultItem KPI data';
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM work_type_kpi wtk
                    WHERE wtk.work_type_id = v_work_type_id
                      AND wtk.kpi_definition_id = v_kpi
                      AND v_date >= wtk.effective_from
                      AND (wtk.effective_to IS NULL OR v_date <= wtk.effective_to)
                ) THEN
                    RAISE EXCEPTION 'KPI % is not applicable to the WorkType of WorkResultItem %', v_kpi, NEW.id;
                END IF;

                IF v_agg = 'RATIO' THEN
                    IF NEW.denominator IS NULL OR NEW.denominator <= 0 THEN
                        RAISE EXCEPTION 'RATIO KPI WorkResultItem requires denominator > 0';
                    END IF;
                ELSIF v_agg <> 'AVG' AND NEW.denominator IS NOT NULL THEN
                    RAISE EXCEPTION 'SUM KPI WorkResultItem must not have denominator';
                END IF;

                RETURN NEW;
            END;
            $fn$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Ledger rows credited from imports have no verification, so they cannot exist once verification_id is NOT NULL again.
        migrationBuilder.Sql("""
            DELETE FROM work_result_item WHERE verified_work_result_id IN (SELECT id FROM verified_work_result WHERE verification_id IS NULL);
            DELETE FROM verified_work_result WHERE verification_id IS NULL;
            """);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION validate_work_result_item() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE
                v_work_type_id UUID;
                v_kpi UUID;
                v_agg kpi_aggregation_type;
            BEGIN
                SELECT t.kpi_definition_id, k.aggregation_type INTO v_kpi, v_agg
                FROM actual_progress ap
                JOIN target_assignment t ON t.id = ap.target_assignment_id
                JOIN kpi_definition k ON k.id = t.kpi_definition_id
                WHERE ap.id = NEW.actual_progress_id;

                SELECT w.work_type_id INTO v_work_type_id
                FROM verified_work_result vwr
                JOIN verification v ON v.id = vwr.verification_id
                JOIN result_submission rs ON rs.id = v.result_submission_id
                JOIN work w ON w.id = rs.work_id
                WHERE vwr.id = NEW.verified_work_result_id;

                IF v_work_type_id IS NULL THEN
                    RAISE EXCEPTION 'Unclassified Work cannot generate WorkResultItem KPI data';
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM work_type_kpi wtk
                    JOIN verified_work_result vwr ON vwr.id = NEW.verified_work_result_id
                    JOIN verification ver ON ver.id = vwr.verification_id
                    WHERE wtk.work_type_id = v_work_type_id
                      AND wtk.kpi_definition_id = v_kpi
                      AND ver.verified_at::date >= wtk.effective_from
                      AND (wtk.effective_to IS NULL OR ver.verified_at::date <= wtk.effective_to)
                ) THEN
                    RAISE EXCEPTION 'KPI % is not applicable to the WorkType of WorkResultItem %', v_kpi, NEW.id;
                END IF;

                IF v_agg = 'RATIO' THEN
                    IF NEW.denominator IS NULL OR NEW.denominator <= 0 THEN
                        RAISE EXCEPTION 'RATIO KPI WorkResultItem requires denominator > 0';
                    END IF;
                ELSE
                    IF NEW.denominator IS NOT NULL THEN
                        RAISE EXCEPTION 'Non-RATIO KPI WorkResultItem must not have denominator';
                    END IF;
                END IF;

                RETURN NEW;
            END;
            $fn$;
            """);
        migrationBuilder.DropForeignKey(
            name: "fk_verified_work_result_work_transaction",
            table: "verified_work_result");

        migrationBuilder.DropTable(
            name: "work_transaction");

        migrationBuilder.DropIndex(
            name: "uq_verified_work_result_work_transaction",
            table: "verified_work_result");

        migrationBuilder.DropColumn(
            name: "value_source",
            table: "work_type_kpi");

        migrationBuilder.DropColumn(
            name: "work_transaction_id",
            table: "verified_work_result");

        migrationBuilder.AlterColumn<Guid>(
            name: "verification_id",
            table: "verified_work_result",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
