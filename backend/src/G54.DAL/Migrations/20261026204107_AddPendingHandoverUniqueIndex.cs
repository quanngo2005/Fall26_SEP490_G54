using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddPendingHandoverUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ux_task_handover_pending_work",
            table: "task_handover",
            column: "work_id",
            unique: true,
            filter: "(status = 'PENDING'::handover_status)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_task_handover_pending_work",
            table: "task_handover");
    }
}
