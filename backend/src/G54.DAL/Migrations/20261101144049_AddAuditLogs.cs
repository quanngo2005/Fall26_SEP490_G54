using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddAuditLogs : Migration
{
    private static readonly string[] EntityIndexColumns = { "entity_name", "entity_id" };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_logs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                entity_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                old_values = table.Column<string>(type: "jsonb", nullable: true),
                new_values = table.Column<string>(type: "jsonb", nullable: true),
                changed_columns = table.Column<string>(type: "jsonb", nullable: true),
                actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                request_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("audit_logs_pkey", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_logs_actor_id",
            table: "audit_logs",
            column: "actor_id");

        migrationBuilder.CreateIndex(
            name: "ix_audit_logs_entity",
            table: "audit_logs",
            columns: EntityIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ix_audit_logs_occurred_at",
            table: "audit_logs",
            column: "occurred_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_logs");
    }
}
