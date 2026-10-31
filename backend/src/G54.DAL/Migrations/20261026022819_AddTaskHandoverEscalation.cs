using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddTaskHandoverEscalation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "escalated_at",
            table: "task_handover",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "escalated_to_employee_id",
            table: "task_handover",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_task_handover_escalated_to",
            table: "task_handover",
            column: "escalated_to_employee_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_handover_escalation",
            table: "task_handover",
            sql: "(escalated_at IS NULL) = (escalated_to_employee_id IS NULL)");

        migrationBuilder.AddForeignKey(
            name: "fk_task_handover_escalated_to",
            table: "task_handover",
            column: "escalated_to_employee_id",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_task_handover_escalated_to",
            table: "task_handover");

        migrationBuilder.DropIndex(
            name: "ix_task_handover_escalated_to",
            table: "task_handover");

        migrationBuilder.DropCheckConstraint(
            name: "ck_task_handover_escalation",
            table: "task_handover");

        migrationBuilder.DropColumn(
            name: "escalated_at",
            table: "task_handover");

        migrationBuilder.DropColumn(
            name: "escalated_to_employee_id",
            table: "task_handover");
    }
}
