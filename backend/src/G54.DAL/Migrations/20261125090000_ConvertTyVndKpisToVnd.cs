using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// The seeded KPIs HDV and DU_NO were stored in <c>tỷ VND</c> (1 = one billion), while every other monetary KPI and the
/// year-target import store full VND. This converts them to <c>VND</c>: multiplies their stored amounts by 10^9 (targets,
/// change log, progress and the results built from it) and sets the unit. Keyed on the unit, so it is idempotent; the same
/// block ends seed_dev_data.sql for a freshly seeded database. Data-only. Down divides back for the same two KPIs.
/// <c>work.target_value</c> is left alone: it is not known to be an amount. User triggers on the four value tables are
/// switched off for the update (they block editing generated results and re-check parent/child sums) and back on after.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261125090000_ConvertTyVndKpisToVnd")]
public partial class ConvertTyVndKpisToVnd : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE target_assignment DISABLE TRIGGER USER;
            ALTER TABLE actual_progress DISABLE TRIGGER USER;
            ALTER TABLE performance_contribution DISABLE TRIGGER USER;
            ALTER TABLE work_result_item DISABLE TRIGGER USER;

            UPDATE target_assignment
            SET target_value = target_value * 1000000000
            WHERE kpi_definition_id IN (SELECT id FROM kpi_definition WHERE code IN ('HDV', 'DU_NO') AND unit = 'tỷ VND');

            UPDATE target_change_log
            SET old_value = old_value * 1000000000, new_value = new_value * 1000000000
            WHERE kpi_definition_id IN (SELECT id FROM kpi_definition WHERE code IN ('HDV', 'DU_NO') AND unit = 'tỷ VND');

            UPDATE performance_contribution
            SET target_value_snapshot = target_value_snapshot * 1000000000,
                actual_value = actual_value * 1000000000,
                weighted_actual_value = weighted_actual_value * 1000000000
            WHERE actual_progress_id IN (
                SELECT ap.id FROM actual_progress ap
                JOIN target_assignment ta ON ta.id = ap.target_assignment_id
                JOIN kpi_definition k ON k.id = ta.kpi_definition_id
                WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'tỷ VND');

            UPDATE work_result_item
            SET value = value * 1000000000, raw_value = raw_value * 1000000000
            WHERE actual_progress_id IN (
                SELECT ap.id FROM actual_progress ap
                JOIN target_assignment ta ON ta.id = ap.target_assignment_id
                JOIN kpi_definition k ON k.id = ta.kpi_definition_id
                WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'tỷ VND');

            UPDATE actual_progress
            SET value = value * 1000000000
            WHERE target_assignment_id IN (
                SELECT ta.id FROM target_assignment ta
                JOIN kpi_definition k ON k.id = ta.kpi_definition_id
                WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'tỷ VND');

            UPDATE kpi_definition
            SET unit = 'VND', baseline_value = baseline_value * 1000000000
            WHERE code IN ('HDV', 'DU_NO') AND unit = 'tỷ VND';

            ALTER TABLE target_assignment ENABLE TRIGGER USER;
            ALTER TABLE actual_progress ENABLE TRIGGER USER;
            ALTER TABLE performance_contribution ENABLE TRIGGER USER;
            ALTER TABLE work_result_item ENABLE TRIGGER USER;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE target_assignment DISABLE TRIGGER USER;
            ALTER TABLE actual_progress DISABLE TRIGGER USER;
            ALTER TABLE performance_contribution DISABLE TRIGGER USER;
            ALTER TABLE work_result_item DISABLE TRIGGER USER;

            UPDATE target_assignment
            SET target_value = target_value / 1000000000
            WHERE kpi_definition_id IN (SELECT id FROM kpi_definition WHERE code IN ('HDV', 'DU_NO') AND unit = 'VND');

            UPDATE target_change_log
            SET old_value = old_value / 1000000000, new_value = new_value / 1000000000
            WHERE kpi_definition_id IN (SELECT id FROM kpi_definition WHERE code IN ('HDV', 'DU_NO') AND unit = 'VND');

            UPDATE performance_contribution
            SET target_value_snapshot = target_value_snapshot / 1000000000,
                actual_value = actual_value / 1000000000,
                weighted_actual_value = weighted_actual_value / 1000000000
            WHERE actual_progress_id IN (
                SELECT ap.id FROM actual_progress ap
                JOIN target_assignment ta ON ta.id = ap.target_assignment_id
                JOIN kpi_definition k ON k.id = ta.kpi_definition_id
                WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'VND');

            UPDATE work_result_item
            SET value = value / 1000000000, raw_value = raw_value / 1000000000
            WHERE actual_progress_id IN (
                SELECT ap.id FROM actual_progress ap
                JOIN target_assignment ta ON ta.id = ap.target_assignment_id
                JOIN kpi_definition k ON k.id = ta.kpi_definition_id
                WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'VND');

            UPDATE actual_progress
            SET value = value / 1000000000
            WHERE target_assignment_id IN (
                SELECT ta.id FROM target_assignment ta
                JOIN kpi_definition k ON k.id = ta.kpi_definition_id
                WHERE k.code IN ('HDV', 'DU_NO') AND k.unit = 'VND');

            UPDATE kpi_definition
            SET unit = 'tỷ VND', baseline_value = baseline_value / 1000000000
            WHERE code IN ('HDV', 'DU_NO') AND unit = 'VND';

            ALTER TABLE target_assignment ENABLE TRIGGER USER;
            ALTER TABLE actual_progress ENABLE TRIGGER USER;
            ALTER TABLE performance_contribution ENABLE TRIGGER USER;
            ALTER TABLE work_result_item ENABLE TRIGGER USER;
            """);
    }
}
