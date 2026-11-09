using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddWorkTypeProductMultiplier : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "multiplier",
            table: "work_type_kpi",
            type: "numeric(6,4)",
            precision: 6,
            scale: 4,
            nullable: false,
            defaultValue: 1.0m);

        migrationBuilder.AddColumn<string>(
            name: "product_code",
            table: "work_type",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "multiplier_applied",
            table: "work_result_item",
            type: "numeric(6,4)",
            precision: 6,
            scale: 4,
            nullable: false,
            defaultValue: 1.0m);

        migrationBuilder.AddColumn<decimal>(
            name: "raw_value",
            table: "work_result_item",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        // Backfill existing rows: before this change every stored value was already the raw (unweighted)
        // amount, so raw_value = value and multiplier_applied defaults to 1.0 (added above).
        migrationBuilder.Sql("UPDATE work_result_item SET raw_value = value;");

        migrationBuilder.AddCheckConstraint(
            name: "ck_work_type_kpi_multiplier",
            table: "work_type_kpi",
            sql: "multiplier > 0 AND multiplier <= 10");

        migrationBuilder.CreateIndex(
            name: "uq_work_type_product_code",
            table: "work_type",
            column: "product_code",
            unique: true,
            filter: "product_code IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_work_result_item_multiplier_applied",
            table: "work_result_item",
            sql: "multiplier_applied > 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_work_result_item_raw_value",
            table: "work_result_item",
            sql: "raw_value >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_work_type_kpi_multiplier",
            table: "work_type_kpi");

        migrationBuilder.DropIndex(
            name: "uq_work_type_product_code",
            table: "work_type");

        migrationBuilder.DropCheckConstraint(
            name: "ck_work_result_item_multiplier_applied",
            table: "work_result_item");

        migrationBuilder.DropCheckConstraint(
            name: "ck_work_result_item_raw_value",
            table: "work_result_item");

        migrationBuilder.DropColumn(
            name: "multiplier",
            table: "work_type_kpi");

        migrationBuilder.DropColumn(
            name: "product_code",
            table: "work_type");

        migrationBuilder.DropColumn(
            name: "multiplier_applied",
            table: "work_result_item");

        migrationBuilder.DropColumn(
            name: "raw_value",
            table: "work_result_item");
    }
}
