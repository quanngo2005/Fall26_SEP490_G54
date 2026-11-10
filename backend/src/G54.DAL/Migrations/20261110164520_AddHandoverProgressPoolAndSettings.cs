using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddHandoverProgressPoolAndSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TYPE handover_status ADD VALUE IF NOT EXISTS 'IN_POOL';");

        migrationBuilder.AddColumn<decimal>(
            name: "progress_percent",
            table: "work",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AlterColumn<Guid>(
            name: "to_employee_id",
            table: "task_handover",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.CreateTable(
            name: "system_setting",
            columns: table => new
            {
                key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("system_setting_pkey", x => x.key);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_work_progress_percent",
            table: "work",
            sql: "progress_percent >= 0 AND progress_percent <= 100");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "system_setting");

        migrationBuilder.DropCheckConstraint(
            name: "ck_work_progress_percent",
            table: "work");

        migrationBuilder.DropColumn(
            name: "progress_percent",
            table: "work");

        migrationBuilder.Sql("DELETE FROM task_handover WHERE to_employee_id IS NULL; UPDATE task_handover SET status = 'CANCELLED' WHERE status::text = 'IN_POOL';");

        migrationBuilder.AlterColumn<Guid>(
            name: "to_employee_id",
            table: "task_handover",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
