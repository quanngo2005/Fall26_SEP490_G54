using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddContributionWeightedActual : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "weighted_actual_value",
            table: "performance_contribution",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        // Until now actual_progress.value held the multiplied amount. Rebuild it as the real amount (sum of
        // raw_value) and keep the old stored actual of each contribution as its points value.
        migrationBuilder.Sql("UPDATE performance_contribution SET weighted_actual_value = actual_value;");
        migrationBuilder.Sql(
            """
            UPDATE actual_progress ap
            SET value = items.raw_total
            FROM (SELECT actual_progress_id, SUM(raw_value) AS raw_total FROM work_result_item GROUP BY actual_progress_id) items
            WHERE items.actual_progress_id = ap.id;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE actual_progress ap
            SET value = items.value_total
            FROM (SELECT actual_progress_id, SUM(value) AS value_total FROM work_result_item GROUP BY actual_progress_id) items
            WHERE items.actual_progress_id = ap.id;
            """);

        migrationBuilder.DropColumn(
            name: "weighted_actual_value",
            table: "performance_contribution");
    }
}
