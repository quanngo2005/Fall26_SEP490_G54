using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddWorkTargetValue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "target_value",
            table: "work",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_work_target_value",
            table: "work",
            sql: "target_value IS NULL OR target_value >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_work_target_value",
            table: "work");

        migrationBuilder.DropColumn(
            name: "target_value",
            table: "work");
    }
}
