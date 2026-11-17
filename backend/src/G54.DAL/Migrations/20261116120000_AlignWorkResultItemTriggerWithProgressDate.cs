using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Fixes <c>validate_work_result_item</c>: the work type ↔ KPI mapping must be checked on the date the application used to
/// pick it, which is <c>actual_progress.progress_date</c> (the transaction date for an import, the business day for a manual
/// approval). The trigger used <c>verified_work_result.verified_at</c> (today), so a mapping effective after today, or a
/// mapping that ended before today for an older transaction, made the insert fail and reconcile returned 500.
/// Data-only; the model is unchanged.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261116120000_AlignWorkResultItemTriggerWithProgressDate")]
public partial class AlignWorkResultItemTriggerWithProgressDate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(FunctionSql("v_date"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(FunctionSql("COALESCE(vwr.verified_at::date, v_date)"));
    }

    private static string FunctionSql(string mappingDate) => $$"""
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
            SELECT w.work_type_id, {{mappingDate}}
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
        """;
}
