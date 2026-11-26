using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddQualityFlagReviewTrail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "dismiss_reason",
            table: "quality_flag",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "reviewed_at",
            table: "quality_flag",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "reviewed_by_employee_id",
            table: "quality_flag",
            type: "uuid",
            nullable: true);

        // Flags reviewed before this migration keep their review time (the detail used to read it from updated_at);
        // the reviewer was not recorded, so it stays empty.
        migrationBuilder.Sql("""
            UPDATE quality_flag
            SET reviewed_at = COALESCE(rework_requested_at, updated_at)
            WHERE status::text <> 'OPEN' AND reviewed_at IS NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_quality_flag_reviewed_by_employee_id",
            table: "quality_flag",
            column: "reviewed_by_employee_id");

        migrationBuilder.AddForeignKey(
            name: "fk_quality_flag_reviewer",
            table: "quality_flag",
            column: "reviewed_by_employee_id",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_quality_flag_reviewer",
            table: "quality_flag");

        migrationBuilder.DropIndex(
            name: "IX_quality_flag_reviewed_by_employee_id",
            table: "quality_flag");

        migrationBuilder.DropColumn(
            name: "dismiss_reason",
            table: "quality_flag");

        migrationBuilder.DropColumn(
            name: "reviewed_at",
            table: "quality_flag");

        migrationBuilder.DropColumn(
            name: "reviewed_by_employee_id",
            table: "quality_flag");
    }
}
