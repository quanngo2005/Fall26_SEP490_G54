using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddTaskHandoverReferrerShare : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "referrer_share_percent",
            table: "task_handover",
            type: "numeric(7,4)",
            precision: 7,
            scale: 4,
            nullable: false,
            defaultValue: 30m);

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_handover_referrer_share",
            table: "task_handover",
            sql: "referrer_share_percent >= 0 AND referrer_share_percent <= 100");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_task_handover_referrer_share",
            table: "task_handover");

        migrationBuilder.DropColumn(
            name: "referrer_share_percent",
            table: "task_handover");
    }
}
