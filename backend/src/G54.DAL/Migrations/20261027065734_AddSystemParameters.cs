using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddSystemParameters : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "system_parameter_changes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                old_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                new_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_system_parameter_changes", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "system_parameters",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                numeric_value = table.Column<long>(type: "bigint", nullable: true),
                text_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                min_value = table.Column<long>(type: "bigint", nullable: true),
                max_value = table.Column<long>(type: "bigint", nullable: true),
                is_locked = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_system_parameters", x => x.id);
            });

#pragma warning disable CA1861
        migrationBuilder.CreateIndex(
            name: "IX_system_parameter_changes_key_changed_at",
            table: "system_parameter_changes",
            columns: new[] { "key", "changed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_system_parameters_key",
            table: "system_parameters",
            column: "key",
            unique: true);

        // Seed default system parameters
        var createdAt = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        migrationBuilder.InsertData(
            table: "system_parameters",
            columns: new[] { "id", "key", "numeric_value", "text_value", "min_value", "max_value", "is_locked", "version", "created_at", "created_by", "updated_at", "updated_by", "is_deleted" },
            values: new object[,]
            {
                { new Guid("00000000-0000-0000-0000-000000000001"), "manualApprovalHours", 48L, null, 1L, 168L, false, 0, createdAt, null, null, null, false },
                { new Guid("00000000-0000-0000-0000-000000000002"), "importMaxFileMb", 5L, null, 1L, 10L, false, 0, createdAt, null, null, null, false },
                { new Guid("00000000-0000-0000-0000-000000000003"), "importFormats", null, ".xlsx", null, null, true, 0, createdAt, null, null, null, false },
                { new Guid("00000000-0000-0000-0000-000000000004"), "kpiLockTime", null, "23:59:59", null, null, true, 0, createdAt, null, null, null, false },
                { new Guid("00000000-0000-0000-0000-000000000005"), "appealWindowDays", 5L, null, 1L, 30L, false, 0, createdAt, null, null, null, false },
                { new Guid("00000000-0000-0000-0000-000000000006"), "preCloseCheckDays", 3L, null, 1L, 30L, false, 0, createdAt, null, null, null, false },
                { new Guid("00000000-0000-0000-0000-000000000007"), "appealRejectedHours", 48L, null, null, null, true, 0, createdAt, null, null, null, false },
                { new Guid("00000000-0000-0000-0000-000000000008"), "handoverThresholdVnd", 50000000L, null, 1L, null, false, 0, createdAt, null, null, null, false },
            });
#pragma warning restore CA1861
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "system_parameter_changes");

        migrationBuilder.DropTable(
            name: "system_parameters");
    }
}
