using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// One KPI detail row per anchoring progress row within an evaluation, so two requests saving the same evaluation at
/// once cannot both insert the row. The table has no KPI column (the KPI is identified through the progress row),
/// and each calculation anchors a KPI row to the latest progress row of that KPI, so the index is on
/// (evaluation, progress) for KPI rows only; risk-event rows are excluded.
/// KPI rows are derived data that every calculation rebuilds. Rows that already share an evaluation and a progress
/// row are removed first, keeping the most recently updated one of each pair, otherwise the index cannot be created.
/// </summary>
public partial class AddPerformanceContributionKpiProgressUniqueIndex : Migration
{
    private static readonly string[] KpiProgressColumns = ["performance_evaluation_id", "actual_progress_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY performance_evaluation_id, actual_progress_id
                               ORDER BY updated_at DESC, id) AS position
                    FROM performance_contribution
                    WHERE risk_event_id IS NULL AND actual_progress_id IS NOT NULL)
                DELETE FROM performance_contribution pc
                USING ranked
                WHERE ranked.id = pc.id AND ranked.position > 1;");

        migrationBuilder.CreateIndex(
            name: "uq_performance_contribution_kpi_progress",
            table: "performance_contribution",
            columns: KpiProgressColumns,
            unique: true,
            filter: "risk_event_id IS NULL AND actual_progress_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "uq_performance_contribution_kpi_progress",
            table: "performance_contribution");
    }
}
