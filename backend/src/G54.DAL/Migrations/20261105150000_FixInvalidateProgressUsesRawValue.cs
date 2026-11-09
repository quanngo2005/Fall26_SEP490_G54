using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Fixes <c>invalidate_source_actual_progress</c>: a day's <c>actual_progress.value</c> is the sum of the RAW amounts of its
/// verified results (the multiplier is applied only when scoring, from <c>work_result_item.value</c>), so invalidating a
/// result must take out its <c>raw_value</c>, not the multiplied <c>value</c>. The row is marked INVALID only when no VALID
/// result points at it any more (not when a subtraction happens to reach zero), so results with different multipliers on the
/// same day stay consistent. Data-only; the model is unchanged.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261105150000_FixInvalidateProgressUsesRawValue")]
public partial class FixInvalidateProgressUsesRawValue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(FunctionSql("raw_value"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restores the previous body (subtracts the multiplied value, invalidates when the subtraction reaches zero).
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION invalidate_source_actual_progress()
            RETURNS TRIGGER
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW.status = 'INVALID' AND OLD.status IS DISTINCT FROM NEW.status THEN
                    UPDATE actual_progress ap
                    SET value = GREATEST(ap.value - s.value, 0),
                        denominator = CASE
                            WHEN ap.denominator IS NULL THEN NULL
                            WHEN ap.denominator - COALESCE(s.denominator, 0) > 0 THEN ap.denominator - COALESCE(s.denominator, 0)
                            ELSE ap.denominator
                        END,
                        status = CASE WHEN ap.value - s.value <= 0 THEN 'INVALID'::actual_progress_status ELSE ap.status END,
                        updated_at = now()
                    FROM (
                        SELECT actual_progress_id, SUM(value) AS value, SUM(denominator) AS denominator
                        FROM work_result_item
                        WHERE verified_work_result_id = NEW.id
                        GROUP BY actual_progress_id
                    ) s
                    WHERE ap.id = s.actual_progress_id;
                END IF;
                RETURN NEW;
            END;
            $$;
            """);
    }

    private static string FunctionSql(string column) => $$"""
        CREATE OR REPLACE FUNCTION invalidate_source_actual_progress()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        BEGIN
            IF NEW.status = 'INVALID' AND OLD.status IS DISTINCT FROM NEW.status THEN
                UPDATE actual_progress ap
                SET value = GREATEST(ap.value - s.amount, 0),
                    denominator = CASE
                        WHEN ap.denominator IS NULL THEN NULL
                        WHEN ap.denominator - COALESCE(s.denominator, 0) > 0 THEN ap.denominator - COALESCE(s.denominator, 0)
                        ELSE ap.denominator
                    END,
                    status = CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM work_result_item other
                            JOIN verified_work_result v ON v.id = other.verified_work_result_id
                            WHERE other.actual_progress_id = ap.id AND v.status = 'VALID')
                        THEN ap.status
                        ELSE 'INVALID'::actual_progress_status
                    END,
                    updated_at = now()
                FROM (
                    SELECT actual_progress_id, SUM({{column}}) AS amount, SUM(denominator) AS denominator
                    FROM work_result_item
                    WHERE verified_work_result_id = NEW.id
                    GROUP BY actual_progress_id
                ) s
                WHERE ap.id = s.actual_progress_id;
            END IF;
            RETURN NEW;
        END;
        $$;
        """;
}
