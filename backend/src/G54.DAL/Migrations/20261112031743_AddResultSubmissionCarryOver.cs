using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddResultSubmissionCarryOver : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "carried_over_to_period_id",
            table: "result_submission",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_result_submission_carried_over_period",
            table: "result_submission",
            column: "carried_over_to_period_id");

        migrationBuilder.AddForeignKey(
            name: "fk_result_submission_carried_over_period",
            table: "result_submission",
            column: "carried_over_to_period_id",
            principalTable: "evaluation_period",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_result_submission_carried_over_period",
            table: "result_submission");

        migrationBuilder.DropIndex(
            name: "ix_result_submission_carried_over_period",
            table: "result_submission");

        migrationBuilder.DropColumn(
            name: "carried_over_to_period_id",
            table: "result_submission");
    }
}
