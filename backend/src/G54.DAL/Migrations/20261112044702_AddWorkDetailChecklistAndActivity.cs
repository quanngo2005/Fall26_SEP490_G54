using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddWorkDetailChecklistAndActivity : Migration
{
    private static readonly string[] ActivityColumns = ["work_id", "created_at"];
    private static readonly string[] ChecklistColumns = ["work_id", "sort_order"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "assigned_at",
            table: "work",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "now()");

        // Rows created before this column existed: the best known time of assignment is the last update.
        migrationBuilder.Sql("UPDATE work SET assigned_at = updated_at;");

        migrationBuilder.AddColumn<string>(
            name: "customer_cif",
            table: "work",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "customer_name",
            table: "work",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "work_activity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                message = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_activity", x => x.id);
                table.ForeignKey(
                    name: "fk_work_activity_actor",
                    column: x => x.actor_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_work_activity_work",
                    column: x => x.work_id,
                    principalTable: "work",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "work_checklist_item",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                is_done = table.Column<bool>(type: "boolean", nullable: false),
                done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_checklist_item", x => x.id);
                table.ForeignKey(
                    name: "fk_work_checklist_item_work",
                    column: x => x.work_id,
                    principalTable: "work",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_work_activity_actor_employee_id",
            table: "work_activity",
            column: "actor_employee_id");

        migrationBuilder.CreateIndex(
            name: "ix_work_activity_work",
            table: "work_activity",
            columns: ActivityColumns);

        migrationBuilder.CreateIndex(
            name: "ix_work_checklist_item_work",
            table: "work_checklist_item",
            columns: ChecklistColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "work_activity");

        migrationBuilder.DropTable(
            name: "work_checklist_item");

        migrationBuilder.DropColumn(
            name: "assigned_at",
            table: "work");

        migrationBuilder.DropColumn(
            name: "customer_cif",
            table: "work");

        migrationBuilder.DropColumn(
            name: "customer_name",
            table: "work");
    }
}
