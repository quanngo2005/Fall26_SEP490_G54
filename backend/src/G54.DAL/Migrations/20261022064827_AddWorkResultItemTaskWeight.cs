using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddWorkResultItemTaskWeight : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "task_weight",
            table: "work_result_item",
            type: "numeric(8,4)",
            precision: 8,
            scale: 4,
            nullable: false,
            defaultValue: 1m);

        migrationBuilder.AddCheckConstraint(
            name: "ck_work_result_item_task_weight",
            table: "work_result_item",
            sql: "task_weight > 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_work_result_item_task_weight",
            table: "work_result_item");

        migrationBuilder.DropColumn(
            name: "task_weight",
            table: "work_result_item");
    }
}
